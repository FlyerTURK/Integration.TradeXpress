using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Metals;

/// <summary>
/// Maden liste önizlemesi (ImagePreviewUrl) REGRESYON ağı — gerçek Sqlite DB ile. Maden-düzeyi owned görsel
/// (<c>Metal.Image</c>) 2026-07-31'de emekli edildi; grid thumbnail'i artık ANA varyantın "MetalVariant"
/// bağlamındaki VARSAYILAN DAM medyasının poster'ından gelir (<c>CommodityAgnosticGraph.GetVariantPreviewMapAsync</c>
/// zinciri — Stone/Jewelry/Good ile aynı desen). KIRMIZIYSA maden grid'i thumbnail'siz kalmış demektir —
/// testi gevşetme, zinciri düzelt.
/// </summary>
public abstract class MetalVariantPreviewTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IMetalAppService _metalAppService;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<Media, Guid> _mediaRepository;
    private readonly ICurrentCompany _currentCompany;

    /// <summary>Emtia SAHİPLİĞİ aktif working company'den gelir (CompanyOwnershipGuard, fail-closed) — fixture'lar
    /// bu yüzden bir çalışma şirketi altında kurulur. Testin KONUSU bu değil (liste önizleme zinciri).</summary>
    private static readonly Guid FixtureCompanyId = Guid.NewGuid();

    protected MetalVariantPreviewTests()
    {
        _metalAppService = GetRequiredService<IMetalAppService>();
        _unitRepository = GetRequiredService<IRepository<CurrencyUnit, Guid>>();
        _mediaRepository = GetRequiredService<IRepository<Media, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task List_preview_fills_from_main_variant_poster_and_stays_null_without_media()
    {
        var unitId = await GetAnyUnitIdAsync();

        using (_currentCompany.Change(FixtureCompanyId))
        {
            // 1) Maden oluştur — nitelik yok → synchronizer ANA varyantı otomatik kurar (kalıcı Id'li).
            var withMedia = await _metalAppService.CreateAsync(new MetalCreateDto
            {
                Code = "VARIMG",
                Name = "Varyant Görselli Maden",
                FollowingUnitId = unitId,
            });
            var mainVariant = withMedia.Variants.ShouldHaveSingleItem();
            mainVariant.IsMain.ShouldBeTrue();
            mainVariant.Id.ShouldNotBe(Guid.Empty);

            // 2) Poster'lı DAM medyası + ana varyanta VARSAYILAN link — public UpdateAsync üzerinden (uçtan uca:
            //    graf save "MetalVariant" bağlamına EntityMediaLink yazar).
            var media = await CreateMediaWithPosterAsync();
            mainVariant.Media.Add(new EntityMediaLinkEditDto { MediaId = media.Id, IsDefault = true });
            await _metalAppService.UpdateAsync(withMedia.Id, new MetalUpdateDto
            {
                Code = withMedia.Code,
                Name = withMedia.Name,
                FollowingUnitId = unitId,
                Factor = withMedia.Factor,
                IsActive = withMedia.IsActive,
                Variants = new List<MetalVariantGraphDto> { mainVariant },
            });

            // 3) Medyasız ikinci maden — kontrol grubu: önizleme null KALMALI (yanlış-pozitif dolduran
            //    bir zenginleştirme burada yakalanır).
            var bare = await _metalAppService.CreateAsync(new MetalCreateDto
            {
                Code = "VARBARE",
                Name = "Medyasız Maden",
                FollowingUnitId = unitId,
            });

            var list = await _metalAppService.GetListAsync(new MetalListRequestDto { MaxResultCount = 1000 });

            // PosterUrl = /api/media/{id}/poster — medya kimliği adreste düz metin geçer (assert çapası).
            var mediaRow = list.Items.Single(x => x.Id == withMedia.Id);
            mediaRow.ImagePreviewUrl.ShouldNotBeNullOrEmpty();
            mediaRow.ImagePreviewUrl!.ShouldContain(media.Id.ToString());

            list.Items.Single(x => x.Id == bare.Id).ImagePreviewUrl.ShouldBeNull();
        }
    }

    // DAM kütüphane kaydı — poster blob adı atanır ki PosterUrl (/api/media/{id}/poster) hesaplansın; blob içeriği
    // GEREKMEZ (GoodHostCatalogPreviewTests deseni). Şirket = fixture şirketi (medya company-scoped görünür).
    private async Task<Media> CreateMediaWithPosterAsync()
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var media = new Media(
                companyId: FixtureCompanyId,
                mediaType: MediaType.Image,
                blobName: "metal-variant-media-blob",
                fileName: "poster.png",
                contentType: "image/png",
                size: 3,
                contentHash: "metal-variant-media-hash");
            media.SetPoster("metal-variant-poster.jpg");
            return await _mediaRepository.InsertAsync(media, autoSave: true);
        });
    }

    /// <summary>Seed'lenmiş herhangi bir para birimi (CurrencyUnitSeeder host kataloğu) — FollowingUnit ZORUNLU.</summary>
    private async Task<Guid> GetAnyUnitIdAsync()
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var units = await _unitRepository.GetListAsync();
            return units.First().Id;
        });
    }
}
