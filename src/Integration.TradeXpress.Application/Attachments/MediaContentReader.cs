using System;
using System.IO;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Attachments;

/// <summary>Bir medyanın içerik baytları + dosya kimliği — geçici-link yayıncısının girdisi.</summary>
public sealed record MediaContentPayload(byte[] Bytes, string FileName, string ContentType);

/// <summary>
/// MEDYA İÇERİĞİNİN UYGULAMA-İÇİ OKUYUCUSU — blob'u bayt olarak verir.
///
/// <para><b>Neden ayrı sınıf:</b> <c>MediaAppService</c> bilinçli olarak bayt DÖNDÜRMEZ (içerik erişimi
/// Id-scoped stream controller'ın işi — HTTP endpoint'lerine bayt açmamak API kararı). Geçici-link yayıncısının
/// ihtiyacı ise UYGULAMA İÇİ okumadır: dışarı açılan bir uç değil, push sırasında blob'u alıp harici
/// barındırmaya yükleyen iç bileşen. O sınır burada yaşar; app service'in sözleşmesi bozulmaz.</para>
/// </summary>
public class MediaContentReader : ITransientDependency
{
    private readonly IRepository<Media, Guid> _mediaRepository;
    private readonly IBlobContainer<MediaContainer> _container;
    private readonly ICurrentCompany _currentCompany;

    public MediaContentReader(
        IRepository<Media, Guid> mediaRepository,
        IBlobContainer<MediaContainer> container,
        ICurrentCompany currentCompany)
    {
        _mediaRepository = mediaRepository;
        _container = container;
        _currentCompany = currentCompany;
    }

    /// <summary>Medyanın içeriğini okur; kayıt ya da blob yoksa <c>null</c> (çağıran atlar — görselin
    /// birini okuyamamak push'un kalanını düşürmez).
    ///
    /// <para><b>Şirket sınırı DERİNLEMESİNE SAVUNMA ile</b> (bağımsız denetim bulgusu, 2026-08-14): kardeş
    /// şirketin medyası bu okuyucudan geçip dış barındırmaya taşınamaz. Repository filtresi görünüm filtresidir
    /// ve bağlama göre gevşeyebilir; bu sınıf baytı dışarı çıkaran uç olduğundan eşitliği kendisi de denetler.
    /// Host medyası (<c>CompanyId == null</c>) paylaşılan katalogdur — okunur.</para></summary>
    public virtual async Task<MediaContentPayload?> ReadAsync(Guid mediaId)
    {
        var media = await _mediaRepository.FindAsync(mediaId);
        if (media is null)
        {
            return null;
        }

        if (media.CompanyId is { } owner && owner != _currentCompany.Id)
        {
            return null;
        }

        var stream = await _container.GetOrNullAsync(media.BlobName);
        if (stream is null)
        {
            return null;
        }

        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return new MediaContentPayload(buffer.ToArray(), media.FileName, media.ContentType);
        }
    }
}
