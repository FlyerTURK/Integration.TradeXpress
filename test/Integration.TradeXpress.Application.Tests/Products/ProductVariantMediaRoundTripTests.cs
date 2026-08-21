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
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürün VARYANT medyasının kaydet/yükle/sil round-trip REGRESYON ağı — public <see cref="IProductAppService"/>
/// üzerinden (client tarafı güven sınırı değildir; ReplaceFor/GetFor zinciri sunucuda durur).
///
/// <para><b>Neden kural:</b> varyant medyası ürün medyasından AYRI bağlamda yaşar
/// (<see cref="MediaEntityNames.ProductVariant"/> + varyant Id'si; link üzerinde varyant kolonu YOKTUR).
/// Kaydeden (<c>SaveProductVariantDetailAsync</c>) ile yükleyen (<c>ProjectVariantsAsync</c>) taraf aynı bağlam
/// anahtarını kullanmazsa medya istisna FIRLATMADAN sessizce "yok" görünür; bağlam ürün-seviyesine sızarsa da
/// pazaryeri push'una yanlış görsel gider. Silmede ise varyant linkleri temizlenmezse yetim link birikir.
/// KIRMIZIYSA bağlam anahtarı kaçağı ya da silme temizliği bozulmuş demektir — testi gevşetme, kök nedeni düzelt.</para>
/// </summary>
public abstract class ProductVariantMediaRoundTripTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IProductAppService _productAppService;
    private readonly IRepository<Media, Guid> _mediaRepository;
    private readonly IRepository<EntityMediaLink, Guid> _linkRepository;
    private readonly ICurrentCompany _currentCompany;

    protected ProductVariantMediaRoundTripTests()
    {
        _productAppService = GetRequiredService<IProductAppService>();
        _mediaRepository = GetRequiredService<IRepository<Media, Guid>>();
        _linkRepository = GetRequiredService<IRepository<EntityMediaLink, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task Variant_media_round_trips_through_public_update_and_stays_variant_scoped()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            // 1) MultiVariant ürün + tek nitelik (Renk: Kırmızı/Mavi) → synchronizer 2 varyant üretir.
            var created = await _productAppService.CreateAsync(new ProductCreateDto
            {
                Code = "TSTVARMEDIA1",
                Name = "Varyant Medya Ürünü",
                ProductCategoryId = await CreateTestProductCategoryAsync(),
                Attributes = new List<EntityAttributeGraphDto> { BuildAttribute("Renk", "Kırmızı", "Mavi") },
            });
            created.Variants.Count.ShouldBe(2);

            // 2) Yeniden yükle, HEDEF varyanta kütüphaneden poster'lı medya link'i bağla (IsDefault=true).
            var got = await _productAppService.GetAsync(created.Id);
            var target = got.Variants.First();
            var other = got.Variants.Single(v => v.Id != target.Id);
            var media = await SeedMediaAsync(companyId, "variant-cover.jpg");
            target.Media.Add(new EntityMediaLinkEditDto { MediaId = media.Id, IsDefault = true });

            await _productAppService.UpdateAsync(created.Id, BuildUpdateDto(got));

            // 3) Round-trip: hedef varyantta TEK link (IsDefault), medya DTO'su kütüphaneden çözülmüş.
            var reloaded = await _productAppService.GetAsync(created.Id);
            var reloadedTarget = reloaded.Variants.Single(v => v.Id == target.Id);
            var link = reloadedTarget.Media.ShouldHaveSingleItem();
            link.MediaId.ShouldBe(media.Id);
            link.IsDefault.ShouldBeTrue();

            // 4) Sızıntı yok: kardeş varyant da ürün-seviyesi bağlam da BOŞ kalır (bağlam anahtarları ayrı).
            reloaded.Variants.Single(v => v.Id == other.Id).Media.ShouldBeEmpty();
            reloaded.Media.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Delete_clears_both_product_and_variant_media_link_contexts()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            // 1) 2 varyantlı ürün; AYNI medya hem ürün-seviyesi hem ilk varyant bağlamına link'lenir
            //    (medya reuse meşru — link'ler per-bağlam ayrı satırlardır).
            var created = await _productAppService.CreateAsync(new ProductCreateDto
            {
                Code = "TSTVARMEDIA2",
                Name = "Varyant Medya Silme Ürünü",
                ProductCategoryId = await CreateTestProductCategoryAsync(),
                Attributes = new List<EntityAttributeGraphDto> { BuildAttribute("Renk", "Kırmızı", "Mavi") },
            });

            var got = await _productAppService.GetAsync(created.Id);
            var media = await SeedMediaAsync(companyId, "delete-cover.jpg");
            got.Media.Add(new EntityMediaLinkEditDto { MediaId = media.Id, IsDefault = true });
            var linkedVariant = got.Variants.First();
            linkedVariant.Media.Add(new EntityMediaLinkEditDto { MediaId = media.Id, IsDefault = true });

            await _productAppService.UpdateAsync(created.Id, BuildUpdateDto(got));

            // 2) Ön-koşul sanity: linkler GERÇEKTEN yazıldı — 0-satır iddiası boş küme üzerinden geçmesin.
            var savedVariantIds = (await _productAppService.GetAsync(created.Id)).Variants.Select(v => v.Id).ToList();
            (await CountLinksAsync(MediaEntityNames.Product, created.Id)).ShouldBe(1);
            (await CountLinksAsync(MediaEntityNames.ProductVariant, linkedVariant.Id)).ShouldBe(1);

            // 3) Silme temizliği: ürün silinince HEM ürün HEM varyant bağlamlarındaki link satırları gider
            //    (medya İÇERİĞİ kütüphanede kalır; yalnız link'ler kalkar).
            await _productAppService.DeleteAsync(created.Id);

            (await CountLinksAsync(MediaEntityNames.Product, created.Id)).ShouldBe(0);
            foreach (var variantId in savedVariantIds)
            {
                (await CountLinksAsync(MediaEntityNames.ProductVariant, variantId)).ShouldBe(0);
            }
        }
    }

    /// <summary>Kütüphaneye medya kaydı ekler (poster'lı — GoodHostCatalogPreviewTests deseni). Blob içeriği
    /// GEREKMEZ: link round-trip'i medya SATIRINA bakar, içeriğe değil.</summary>
    private async Task<Media> SeedMediaAsync(Guid companyId, string fileName)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var media = new Media(
                companyId,
                MediaType.Image,
                blobName: Guid.NewGuid().ToString("N"),
                fileName: fileName,
                contentType: "image/jpeg",
                size: 1024,
                contentHash: Guid.NewGuid().ToString("N"));
            media.SetPoster("variant-media-poster.jpg");
            return await _mediaRepository.InsertAsync(media, autoSave: true);
        });
    }

    /// <summary>Bir bağlamın (EntityName + EntityId) görünür link satır sayısı — public repository API'si
    /// (soft-delete filtresi AÇIK: temizlik ister hard ister soft olsun kullanıcıya 0 satır görünmeli).</summary>
    private async Task<int> CountLinksAsync(string entityName, Guid entityId)
    {
        return await WithUnitOfWorkAsync(() => _linkRepository.CountAsync(
            l => l.EntityName == entityName && l.EntityId == entityId));
    }

    private static ProductUpdateDto BuildUpdateDto(ProductGetDto p)
    {
        // GetAsync dönüşünden elle kurulum — ProductVariantExtensionSurvivalTests.ToUpdateDto deseni
        // (varyant Id'leri aynı nitelik grafıyla resync boyunca korunur).
        return new ProductUpdateDto
        {
            Code = p.Code,
            Name = p.Name,
            IsActive = p.IsActive,
            ProductCategoryId = p.ProductCategoryId,
            Media = p.Media,
            Attributes = p.Attributes,
            Variants = p.Variants,
        };
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
