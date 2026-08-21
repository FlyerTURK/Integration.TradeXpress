using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Goods;

/// <summary>
/// Host-katalog Good önizleme/fiyat REGRESYON ağı (A7 bulgusu; MetalImagePreviewTests deseni) — gerçek Sqlite DB ile:
/// host-düzeyi (TenantId=null) bir mamülün ana-varyant poster'ı (<c>ImagePreviewUrl</c>) ve ana-varyant fiyatı
/// (<c>EntryPrice</c>) TENANT bağlamındaki <c>GetListAsync</c>'te de dolmalı. Kök kusur: zenginleştirme base'in
/// <c>IMultiTenant</c>-disable scope'u KAPANDIKTAN sonra yapılırsa host varyant/medya/fiyat satırları tenant
/// filtresine takılır → thumbnail/fiyat hep boş kalırdı (düzeltme: zenginleştirme <c>EnrichListAsync</c> hook'unda,
/// scope İÇİNDE — kardeş Metal/Jewelry/Stone deseni). KIRMIZIYSA filtre-bağlamı kaçağı geri gelmiş demektir —
/// testi gevşetme, kök nedeni düzelt.
/// </summary>
public abstract class GoodHostCatalogPreviewTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IGoodAppService _goodAppService;
    private readonly IRepository<Media, Guid> _mediaRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentCompany _currentCompany;

    /// <summary>Emtia SAHİPLİĞİ artık aktif working company'den gelir (CompanyOwnershipGuard, fail-closed) —
    /// fixture kurulumu bu yüzden bir çalışma şirketi altında yapılır. Testin KONUSU bu değil: konu, HOST
    /// (TenantId=null) satırların zenginleştirmesinin tenant filtresine takılmaması.</summary>
    private static readonly Guid FixtureCompanyId = Guid.NewGuid();

    protected GoodHostCatalogPreviewTests()
    {
        _goodAppService = GetRequiredService<IGoodAppService>();
        _mediaRepository = GetRequiredService<IRepository<Media, Guid>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task Host_good_thumbnail_and_price_fill_in_tenant_context_listing()
    {
        // 1) HOST bağlamı (ambient test context'i host) — kütüphaneye poster'lı bir medya kaydı eklenir.
        //    Blob içeriği GEREKMEZ: PosterUrl, PosterBlobName'den hesaplanan endpoint'tir (/api/media/{id}/poster).
        var media = await CreateHostMediaWithPosterAsync();

        // 2) HOST mamül + tek nitelik → sunucu (synchronizer) ana varyantı otomatik üretir.
        //    Sahiplik working company'den gelir (fail-closed guard) → fixture bir şirket altında kurulur;
        //    TenantId host bağlamından geldiği için kayıt HOST olmaya devam eder (testin konusu bu).
        GoodGetDto created;
        using (_currentCompany.Change(FixtureCompanyId))
        {
            created = await _goodAppService.CreateAsync(new GoodCreateDto
            {
                Code = "HOSTGOOD",
                Name = "Host Katalog Mamülü",
                Attributes = new List<EntityAttributeGraphDto> { BuildAttribute("Renk", "Kırmızı") },
            });
        }

        var got = await _goodAppService.GetAsync(created.Id);
        var main = got.Variants.ShouldHaveSingleItem();
        main.IsMain.ShouldBeTrue();

        // 3) Ana varyanta VARSAYILAN medya link'i + alış fiyatı bağla (public UpdateAsync üzerinden — uçtan uca).
        main.Media.Add(new EntityMediaLinkEditDto { MediaId = media.Id, IsDefault = true });
        main.EntryPrice = 100m;
        await _goodAppService.UpdateAsync(created.Id, new GoodUpdateDto
        {
            Code = got.Code,
            Name = got.Name,
            IsActive = got.IsActive,
            Attributes = got.Attributes,
            Variants = new List<GoodVariantGraphDto> { main },
        });

        // 4) HOST listesi (sanity) — thumbnail + fiyat dolu.
        var hostRow = await GetListRowAsync(created.Id);
        hostRow.ImagePreviewUrl.ShouldNotBeNullOrEmpty();
        hostRow.ImagePreviewUrl!.ShouldContain(media.Id.ToString());
        hostRow.EntryPrice.ShouldBe(100m);

        // 5) TENANT bağlamında listele — asıl regresyon: host mamülün varyant/medya/fiyat satırları
        //    tenant filtresine TAKILMAMALI (zenginleştirme IMultiTenant-disable scope'u İÇİNDE çalışmalı).
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var tenantRow = await GetListRowAsync(created.Id);
            tenantRow.ImagePreviewUrl.ShouldNotBeNullOrEmpty();
            tenantRow.ImagePreviewUrl!.ShouldContain(media.Id.ToString());
            tenantRow.EntryPrice.ShouldBe(100m);
        }
    }

    // Host medya kaydı (TenantId=null, CompanyId=null) — poster blob adı atanır ki PosterUrl hesaplansın.
    private async Task<Media> CreateHostMediaWithPosterAsync()
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var media = new Media(
                companyId: null,
                mediaType: MediaType.Image,
                blobName: "host-good-media-blob",
                fileName: "poster.png",
                contentType: "image/png",
                size: 3,
                contentHash: "host-good-media-hash");
            media.SetPoster("host-good-poster.jpg");
            return await _mediaRepository.InsertAsync(media, autoSave: true);
        });
    }

    private async Task<GoodListDto> GetListRowAsync(Guid goodId)
    {
        var list = await _goodAppService.GetListAsync(new GoodListRequestDto { MaxResultCount = 1000 });
        return list.Items.Single(x => x.Id == goodId);
    }

    private static EntityAttributeGraphDto BuildAttribute(string name, params string[] values)
    {
        return new EntityAttributeGraphDto
        {
            Name = name,
            Values = values.Select(v => new EntityAttributeValueGraphDto { Value = v }).ToList(),
        };
    }
}
