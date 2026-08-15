using System;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// <see cref="MediaContentReader"/> — geçici-link yayıncısının "okunamayan görsel atlanır" duruşu bu sınıfın
/// <c>null</c> sözleşmesine yaslanır; sözleşme değişse (ör. blob yokken fırlatma) yayıncı testleri sahte
/// okuyucuyla yeşil kalırdı (bağımsız denetim bulgusu). Üç dal gerçek konteynerle pinlenir: kayıt yok →
/// null · gerçek yükleme → bayt+ad+tür birebir · başka şirketin medyası → görünmez (null; şirket sınırı).
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class MediaContentReaderTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string TransparentPixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private readonly MediaContentReader _reader;
    private readonly IMediaAppService _mediaService;
    private readonly ICurrentCompany _currentCompany;

    public MediaContentReaderTests()
    {
        _reader = GetRequiredService<MediaContentReader>();
        _mediaService = GetRequiredService<IMediaAppService>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task Unknown_media_reads_as_null()
    {
        (await WithUnitOfWorkAsync(async () => await _reader.ReadAsync(Guid.NewGuid()))).ShouldBeNull();
    }

    [Fact]
    public async Task Uploaded_media_reads_back_bytes_name_and_content_type()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var bytes = Convert.FromBase64String(TransparentPixelPng);
            var media = await WithUnitOfWorkAsync(async () => await _mediaService.UploadAsync(new MediaUploadDto
            {
                FileName = "okuyucu.png",
                Content = bytes,
            }));

            var payload = await WithUnitOfWorkAsync(async () => await _reader.ReadAsync(media.Id));

            payload.ShouldNotBeNull();
            payload!.FileName.ShouldBe(media.FileName);
            payload.ContentType.ShouldBe(media.ContentType);
            payload.Bytes.Length.ShouldBeGreaterThan(0);
        }
    }

    /// <summary>Şirket sınırı: TENANT bağlamında (canlı koşul) kardeş şirketin medyası okuyucuya görünmez —
    /// repository'nin <c>ICompanyScoped</c> filtresi. Filtre HOST (TenantId=null) kaydını bilinçle muaf tutar
    /// (host medyası paylaşılan katalog); test bu yüzden tenant bağlamında koşar — ilk hâli host bağlamındaydı
    /// ve "sızıyor" sanılan şey filtrenin belgeli host-muafiyetiydi.</summary>
    [Fact]
    public async Task Media_of_another_company_is_invisible_to_the_reader_inside_a_tenant()
    {
        var currentTenant = GetRequiredService<ICurrentTenant>();
        var ownerCompany = Guid.NewGuid();
        Guid mediaId;
        using (currentTenant.Change(Guid.NewGuid()))
        {
            using (_currentCompany.Change(ownerCompany))
            {
                mediaId = (await WithUnitOfWorkAsync(async () => await _mediaService.UploadAsync(new MediaUploadDto
                {
                    FileName = "sirket-a.png",
                    Content = Convert.FromBase64String(TransparentPixelPng),
                }))).Id;

                // Sahibi okur.
                (await WithUnitOfWorkAsync(async () => await _reader.ReadAsync(mediaId))).ShouldNotBeNull();
            }

            using (_currentCompany.Change(Guid.NewGuid()))
            {
                // Kardeş şirket okuyamaz — yayıncı yabancı görseli dış barındırmaya taşıyamaz.
                (await WithUnitOfWorkAsync(async () => await _reader.ReadAsync(mediaId))).ShouldBeNull();
            }
        }
    }
}
