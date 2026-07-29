using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Pazaryerine GİDECEK medya seçimi — <c>GetPushMediaAsync</c> sözleşmesi.
///
/// <para><b>Neden var:</b> push'un görsel kaynağı legacy <c>ProductImage</c>'dan DAM'a taşındı. Buradaki üç kural
/// (kapak önce · pasif elenir · tür süzülür) bozulduğunda hiçbir istisna fırlamaz: pazaryerinde vitrin görseli
/// sessizce değişir, kullanıcının gizlediği görsel yayına çıkar ya da görsel listesine mp4 sızıp XML reddedilir.
/// Düzenleme yüzeyinin kullandığı <c>GetForAsync</c> bu üç kuralın HİÇBİRİNİ uygulamaz — ayrım kasıtlıdır.</para>
/// </summary>
public abstract class EntityMediaPushSelectionTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string OwnerEntityName = MediaEntityNames.Product;

    private readonly IEntityMediaAppService _entityMedia;
    private readonly IRepository<Media, Guid> _mediaRepository;
    private readonly IRepository<EntityMediaLink, Guid> _linkRepository;
    private readonly ICurrentCompany _currentCompany;

    protected EntityMediaPushSelectionTests()
    {
        _entityMedia = GetRequiredService<IEntityMediaAppService>();
        _mediaRepository = GetRequiredService<IRepository<Media, Guid>>();
        _linkRepository = GetRequiredService<IRepository<EntityMediaLink, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task Cover_comes_first_even_when_its_display_order_is_last()
    {
        // DAM'da IsDefault ile DisplayOrder BAĞIMSIZDIR: kullanıcı 3. sıradaki görseli kapak seçebilir.
        // Sıralama push tarafında açıkça uygulanmazsa pazaryerinde vitrin görseli değişir.
        var companyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        using (_currentCompany.Change(companyId))
        {
            await SeedAsync(companyId, ownerId, new[]
            {
                ("a", MediaType.Image, 0, false, true),
                ("b", MediaType.Image, 1, false, true),
                ("cover", MediaType.Image, 2, true, true),   // kapak EN SONDA
            });

            var result = await _entityMedia.GetPushMediaAsync(OwnerEntityName, ownerId, MediaType.Image);

            (await NamesOfAsync(result)).ShouldBe(new[] { "cover", "a", "b" });
        }
    }

    [Fact]
    public async Task Inactive_links_are_excluded()
    {
        // Pasif link düzenleme yüzeyinde GÖRÜNÜR (kullanıcı geri açabilsin) ama pazaryerine GİTMEZ.
        var companyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        using (_currentCompany.Change(companyId))
        {
            await SeedAsync(companyId, ownerId, new[]
            {
                ("visible", MediaType.Image, 0, true, true),
                ("hidden", MediaType.Image, 1, false, false),
            });

            var result = await _entityMedia.GetPushMediaAsync(OwnerEntityName, ownerId, MediaType.Image);

            (await NamesOfAsync(result)).ShouldBe(new[] { "visible" });

            // Düzenleme yüzeyi ikisini de görmeye devam etmeli — filtre yalnız push'a ait.
            var editSet = await _entityMedia.GetForAsync(OwnerEntityName, ownerId);
            editSet.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Video_does_not_leak_into_the_image_set()
    {
        // Görsel listesine mp4 girerse pazaryeri (N11 XML) ürünü reddeder — tür süzgeci zorunlu.
        var companyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        using (_currentCompany.Change(companyId))
        {
            await SeedAsync(companyId, ownerId, new[]
            {
                ("clip", MediaType.Video, 0, true, true),   // video üstelik KAPAK
                ("photo", MediaType.Image, 1, false, true),
            });

            var images = await _entityMedia.GetPushMediaAsync(OwnerEntityName, ownerId, MediaType.Image);
            (await NamesOfAsync(images)).ShouldBe(new[] { "photo" });

            // Video isteyen kanal (Etsy) aynı ucu tür vererek çağırır.
            var videos = await _entityMedia.GetPushMediaAsync(OwnerEntityName, ownerId, MediaType.Video);
            (await NamesOfAsync(videos)).ShouldBe(new[] { "clip" });

            // Tür verilmezse ayrım yapılmaz (kapak yine önce).
            var all = await _entityMedia.GetPushMediaAsync(OwnerEntityName, ownerId);
            all.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task Other_owners_media_is_not_returned()
    {
        // EntityId sızıntısı = başka ürünün görselinin yayınlanması.
        var companyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var otherOwnerId = Guid.NewGuid();

        using (_currentCompany.Change(companyId))
        {
            await SeedAsync(companyId, ownerId, new[] { ("mine", MediaType.Image, 0, true, true) });
            await SeedAsync(companyId, otherOwnerId, new[] { ("theirs", MediaType.Image, 0, true, true) });

            var result = await _entityMedia.GetPushMediaAsync(OwnerEntityName, ownerId, MediaType.Image);

            (await NamesOfAsync(result)).ShouldBe(new[] { "mine" });
        }
    }

    /// <summary>Medya + link çifti kurar. Demet: (ad, tür, sıra, kapak mı, aktif mi).</summary>
    private async Task SeedAsync(
        Guid companyId,
        Guid ownerId,
        IReadOnlyList<(string Name, MediaType Type, int Order, bool IsDefault, bool IsActive)> items)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var item in items)
            {
                var media = await _mediaRepository.InsertAsync(
                    new Media(
                        companyId,
                        item.Type,
                        blobName: Guid.NewGuid().ToString("N"),
                        fileName: item.Name,
                        contentType: item.Type == MediaType.Video ? "video/mp4" : "image/jpeg",
                        size: 1024,
                        contentHash: Guid.NewGuid().ToString("N")),
                    autoSave: true);

                await _linkRepository.InsertAsync(
                    new EntityMediaLink(companyId, OwnerEntityName, ownerId, media.Id, item.Order, item.IsDefault, item.IsActive),
                    autoSave: true);
            }
        });
    }

    /// <summary>Sonucu dosya adlarına çevirir — sıra assert'i okunur olsun diye (PushMediaDto ad taşımaz).</summary>
    private async Task<List<string>> NamesOfAsync(List<PushMediaDto> result)
    {
        var media = await WithUnitOfWorkAsync(async () =>
            await _mediaRepository.GetListAsync(m => result.Select(r => r.MediaId).Contains(m.Id)));

        var nameById = media.ToDictionary(m => m.Id, m => m.FileName);
        return result.Select(r => nameById[r.MediaId]).ToList();
    }
}
