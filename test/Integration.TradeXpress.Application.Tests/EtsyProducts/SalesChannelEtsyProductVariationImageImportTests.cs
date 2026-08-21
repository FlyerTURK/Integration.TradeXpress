using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy VARYASYON FOTOĞRAFI içe aktarımı — uçtan uca, sahte istemciyle (ağ YOK).
///
/// <para><b>Konu:</b> Etsy fotoğrafı bir varyasyon DEĞERİNE bağlar ("Renk=Kırmızı"), bizim varyantlarımız ise
/// KOMBİNASYON başınadır (Renk×Beden). Doğru çeviri, o değeri taşıyan TÜM varyantlara aynı fotoğrafı bağlamaktır;
/// yanlış çeviri ise fotoğrafı tek bir kombinasyona hapsetmek ya da (daha kötüsü) kimlik yokken metne bakıp
/// TAHMİN etmektir. Bu testler ikisini de kapatır.</para>
///
/// <para><b>Neden hatasız kaybedilebilir bir alan:</b> push zinciri varyant→kayıt-geneli fallback'iyle okur —
/// varyant fotoğrafı hiç inmemişse kanal yine bir fotoğraf görür ve hiçbir yerde hata çıkmaz; yalnız YANLIŞ
/// fotoğraf gider.</para>
/// </summary>
public abstract class SalesChannelEtsyProductVariationImageImportTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    // Agnostik varyant tablosunda Product varyantları bu sahip-adıyla tutulur (production: ProductEntityName).
    private const string ProductEntityName = "Product";

    private const long ColorPropertyId = 200;
    private const long RedValueId = 71;
    private const long BlueValueId = 72;
    private const long SizePropertyId = 300;
    private const long SmallValueId = 11;
    private const long MediumValueId = 12;

    private const long RedImageId = 9911;
    private const long BlueImageId = 9912;
    private const string RedImageUrl = "https://cdn.example.com/etsy-kirmizi.jpg";
    private const string BlueImageUrl = "https://cdn.example.com/etsy-mavi.jpg";

    private readonly ISalesChannelEtsyProductAppService _appService;
    private readonly FakeEtsyProductClient _fakeClient;
    private readonly IRepository<SalesChannelEtsy, Guid> _channelRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<EntityMediaLink, Guid> _mediaLinkRepository;
    private readonly ICurrentCompany _currentCompany;

    protected SalesChannelEtsyProductVariationImageImportTests()
    {
        _appService = GetRequiredService<ISalesChannelEtsyProductAppService>();
        _fakeClient = GetRequiredService<FakeEtsyProductClient>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelEtsy, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _variantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _mediaLinkRepository = GetRequiredService<IRepository<EntityMediaLink, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    // ── ③ Bir değere bağlı fotoğraf, o değeri taşıyan TÜM varyantlara iner ──────────────────────────

    /// <summary>"Renk=Kırmızı" fotoğrafı kırmızının HER bedenine iner; mavinin fotoğrafı yalnız maviye. Etsy'nin
    /// modeli (fotoğraf ↔ değer) ile bizim modelimizin (varyant ↔ kombinasyon) çevirisi budur.
    ///
    /// <para>İkinci içe aktarım İDEMPOTENT'tir: aynı fotoğraf ikinci kez bağlanmaz (indirici içerik-hash dedup'ı
    /// aynı medya kimliğini verir) — tekrar eden mağaza senkronu galeriyi şişirmez.</para></summary>
    [Fact]
    public async Task Variation_image_lands_on_every_variant_that_carries_the_value()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "VIMG1");
            SeedRedBlueListing(listingId: 9101, withPropertyIdentity: true, withVariationImages: true);

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            report.CreatedProducts.ShouldBe(1);
            report.UnmappedVariationImages.ShouldBe(0);

            var variants = await LoadVariantsAsync(companyId);
            var redSmall = await LoadVariantMediaLinksAsync(variants["SKU-RED-S"].Id);
            var redMedium = await LoadVariantMediaLinksAsync(variants["SKU-RED-M"].Id);
            var blueSmall = await LoadVariantMediaLinksAsync(variants["SKU-BLUE-S"].Id);

            // Kırmızının İKİ bedeni de AYNI fotoğrafı taşır — Etsy fotoğrafı bedene değil RENGE bağladı.
            redSmall.ShouldHaveSingleItem();
            redMedium.ShouldHaveSingleItem();
            redSmall.Single().MediaId.ShouldBe(redMedium.Single().MediaId);

            // ...ve mavi ÇAPRAZ SIZMA almaz: kendi fotoğrafı, kırmızınınkinden farklı.
            blueSmall.ShouldHaveSingleItem();
            blueSmall.Single().MediaId.ShouldNotBe(redSmall.Single().MediaId);

            // Her bağlamın kendi cover'ı var (varyantın vitrini kendi fotoğrafıdır).
            redSmall.Count(l => l.IsDefault).ShouldBe(1);
            blueSmall.Count(l => l.IsDefault).ShouldBe(1);

            // İkinci tur: bağ sayısı ARTMAZ (idempotent).
            await _appService.ImportFromMarketplaceAsync(channel.Id);
            (await LoadVariantMediaLinksAsync(variants["SKU-RED-S"].Id)).Count.ShouldBe(1);
            (await LoadVariantMediaLinksAsync(variants["SKU-RED-M"].Id)).Count.ShouldBe(1);
            (await LoadVariantMediaLinksAsync(variants["SKU-BLUE-S"].Id)).Count.ShouldBe(1);
        }
    }

    // ── ④ Varyasyon fotoğrafı olmayan listeleme ─────────────────────────────────────────────────────

    /// <summary>Varyasyon fotoğrafı OLMAYAN listeleme normaldir (fotoğraflar yalnız kayıt geneli galeride durur):
    /// içe aktarım başarılıdır, hiçbir varyant medyası yazılmaz ve rapora "eşleşmedi" satırı düşmez — aksi hâlde
    /// mağazanın çoğunluğu her turda yanlış bir uyarı üretirdi.</summary>
    [Fact]
    public async Task Listing_without_variation_images_writes_no_variant_media()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "VIMG2");
            SeedRedBlueListing(listingId: 9102, withPropertyIdentity: true, withVariationImages: false);

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            report.CreatedProducts.ShouldBe(1);
            report.UnmappedVariationImages.ShouldBe(0);

            var variants = await LoadVariantsAsync(companyId);
            foreach (var variant in variants.Values)
            {
                (await LoadVariantMediaLinksAsync(variant.Id)).ShouldBeEmpty();
            }

            // Kayıt geneli galeri ETKİLENMEZ — listelemenin fotoğrafları ürüne yine indi.
            (await LoadProductMediaLinksAsync(await LoadSingleProductIdAsync(companyId))).Count.ShouldBe(2);
        }
    }

    // ── ⑤ Kimlik yoksa UYDURMA EŞLEŞME YOK ──────────────────────────────────────────────────────────

    /// <summary>Offering'in property'si <c>property_id</c>/<c>value_id</c> taşımıyorsa varyant görseli İNMEZ —
    /// metin (ad/değer) eşleşmesine düşülmez.
    ///
    /// <para><b>Neden:</b> Etsy'de aynı görünen iki değer farklı eksenlere ait olabilir; fotoğrafı yanlış varyanta
    /// bağlamak, hiç bağlamamaktan çok daha zor fark edilir (push zinciri fallback'le okuduğu için hata da
    /// vermez). Kayıp SESSİZ değildir: rapora sayılır ve tek satırlık uyarı düşer.</para></summary>
    [Fact]
    public async Task Missing_property_identity_never_guesses_a_match()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "VIMG3");
            SeedRedBlueListing(listingId: 9103, withPropertyIdentity: false, withVariationImages: true);

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            var variants = await LoadVariantsAsync(companyId);
            foreach (var variant in variants.Values)
            {
                (await LoadVariantMediaLinksAsync(variant.Id)).ShouldBeEmpty();
            }

            // İki bağ da eşleşemedi → sayaç + okunabilir uyarı satırı (sessiz geçilmez).
            report.UnmappedVariationImages.ShouldBe(2);
            report.Warnings.ShouldNotBeEmpty();
        }
    }

    // ── Dayanıklılık: uç patlarsa içe aktarım DURMAZ ────────────────────────────────────────────────

    /// <summary>Varyasyon fotoğrafı ucu patlarsa yalnız o listelemenin VARYANT görselleri atlanır; ürün/varyant
    /// zinciri ve kayıt geneli galeri normal biter. Round-trip'in asıl konusu ürün/varyanttır — fotoğraf uğruna
    /// mağazanın tamamını kaybetmeyiz (görsel dalının mevcut sözleşmesi).</summary>
    [Fact]
    public async Task A_failing_variation_image_endpoint_does_not_stop_the_import()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "VIMG4");
            SeedRedBlueListing(listingId: 9104, withPropertyIdentity: true, withVariationImages: true);
            _fakeClient.FailVariationImages = true;

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            report.CreatedProducts.ShouldBe(1);
            report.CreatedVariants.ShouldBe(3);

            var variants = await LoadVariantsAsync(companyId);
            foreach (var variant in variants.Values)
            {
                (await LoadVariantMediaLinksAsync(variant.Id)).ShouldBeEmpty();
            }

            (await LoadProductMediaLinksAsync(await LoadSingleProductIdAsync(companyId))).Count.ShouldBe(2);
        }
    }

    /// <summary>TAŞIMA KATMANI arızası (zaman aşımı / ağ) da içe aktarımı DURDURMAZ.
    ///
    /// <para><b>Neden ayrı test:</b> yukarıdaki kardeşi ucu dostane bir <c>BusinessException</c> ile patlatır; oysa
    /// gerçek hayatta en olası arıza zaman aşımıdır ve o <c>TaskCanceledException</c>'dır. Üretim tarafı yalnız
    /// <c>BusinessException</c> yakalasaydı kardeş test YEŞİL kalır, canlıda ise istisna içe aktarımdan dışarı
    /// çıkıp UoW'u rollback ederek mağazanın o ana kadar işlenmiş TÜM listelemelerini kaybettirirdi. Bu çağrı DB
    /// yazımlarının ortasında ve listeleme başına yapıldığı için risk en yüksek burada.</para></summary>
    [Fact]
    public async Task A_transport_failure_on_the_variation_image_endpoint_does_not_stop_the_import()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "VIMG5");
            SeedRedBlueListing(listingId: 9105, withPropertyIdentity: true, withVariationImages: true);
            _fakeClient.FailVariationImagesWithTransportError = true;

            var report = await _appService.ImportFromMarketplaceAsync(channel.Id);

            report.CreatedProducts.ShouldBe(1);
            report.CreatedVariants.ShouldBe(3);

            var variants = await LoadVariantsAsync(companyId);
            foreach (var variant in variants.Values)
            {
                (await LoadVariantMediaLinksAsync(variant.Id)).ShouldBeEmpty();
            }

            // Kayıt geneli galeri arızadan ETKİLENMEZ (varyant dalı ondan bağımsızdır).
            (await LoadProductMediaLinksAsync(await LoadSingleProductIdAsync(companyId))).Count.ShouldBe(2);
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Kırmızı-S / Kırmızı-M / Mavi-S offering'li tek listeleme kurar. <paramref name="withPropertyIdentity"/>
    /// false ise property'ler yalnız METİN taşır (kimliksiz yanıt benzetimi); <paramref name="withVariationImages"/>
    /// false ise uç boş liste döner (varyasyon fotoğrafı olmayan listeleme).</summary>
    private void SeedRedBlueListing(long listingId, bool withPropertyIdentity, bool withVariationImages)
    {
        _fakeClient.RemoteListings.Clear();
        _fakeClient.VariationImagesByListingId.Clear();
        _fakeClient.VariationImageCalls.Clear();
        _fakeClient.FailVariationImages = false;
        _fakeClient.FailVariationImagesWithTransportError = false;

        var offerings = new List<EtsyRemoteOffering>
        {
            BuildOffering(4001, "SKU-RED-S",
                BuildProperty("Renk", "Kırmızı", ColorPropertyId, RedValueId, withPropertyIdentity),
                BuildProperty("Beden", "S", SizePropertyId, SmallValueId, withPropertyIdentity)),
            BuildOffering(4002, "SKU-RED-M",
                BuildProperty("Renk", "Kırmızı", ColorPropertyId, RedValueId, withPropertyIdentity),
                BuildProperty("Beden", "M", SizePropertyId, MediumValueId, withPropertyIdentity)),
            BuildOffering(4003, "SKU-BLUE-S",
                BuildProperty("Renk", "Mavi", ColorPropertyId, BlueValueId, withPropertyIdentity),
                BuildProperty("Beden", "S", SizePropertyId, SmallValueId, withPropertyIdentity)),
        };

        var images = new List<EtsyRemoteImage>
        {
            new(RedImageId, RedImageUrl),
            new(BlueImageId, BlueImageUrl),
        };

        _fakeClient.RemoteListings.Add(new EtsyRemoteListing(
            ListingId: listingId,
            Title: "Deri Kılıf",
            Description: null,
            Tags: Array.Empty<string>(),
            Materials: Array.Empty<string>(),
            TaxonomyId: null,
            WhoMade: null,
            WhenMade: null,
            ListingType: EtsyListingType.Physical,
            Images: images,
            CurrencyCode: null,
            Offerings: offerings));

        if (withVariationImages)
        {
            _fakeClient.VariationImagesByListingId[listingId] = new List<EtsyVariationImage>
            {
                new(ColorPropertyId, RedValueId, RedImageId),
                new(ColorPropertyId, BlueValueId, BlueImageId),
            };
        }
    }

    private static EtsyRemoteOffering BuildOffering(long etsyProductId, string sku, params EtsyRemoteProperty[] properties)
    {
        return new EtsyRemoteOffering(sku, 5, 100m, true, etsyProductId, properties);
    }

    private static EtsyRemoteProperty BuildProperty(string name, string value, long propertyId, long valueId, bool withIdentity)
    {
        return withIdentity
            ? new EtsyRemoteProperty(name, value, propertyId, valueId)
            : new EtsyRemoteProperty(name, value);
    }

    private async Task<SalesChannelEtsy> SeedChannelAsync(Guid companyId, string suffix)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var channel = new SalesChannelEtsy(companyId, $"ETSY-{suffix}", $"Etsy Kanal {suffix}", "keystring", "shared-secret");
            channel.SetShopInfo("55501", $"Shop {suffix}");
            return await _channelRepository.InsertAsync(channel, autoSave: true);
        });
    }

    private async Task<Guid> LoadSingleProductIdAsync(Guid companyId)
    {
        var products = await WithUnitOfWorkAsync(async () =>
            await _productRepository.GetListAsync(p => p.CompanyId == companyId));
        return products.ShouldHaveSingleItem().Id;
    }

    /// <summary>Şirketin varyantlarını KOD anahtarıyla verir (kod = offering sku'sundan normalize edilir).</summary>
    private async Task<Dictionary<string, EntityVariant>> LoadVariantsAsync(Guid companyId)
    {
        var productId = await LoadSingleProductIdAsync(companyId);
        var variants = await WithUnitOfWorkAsync(async () =>
            await _variantRepository.GetListAsync(v => v.EntityName == ProductEntityName && v.EntityId == productId));
        return variants.ToDictionary(v => v.Code, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Varyantın DAM bağlarını DOĞRUDAN tablodan okur. <c>GetForAsync</c> kullanılmaz: testte kütüphane
    /// kaydı açılmadığından (bkz. <c>FakeMarketplaceImageDownloader</c>) o yol bağları "yetim" sayıp elerdi.</summary>
    private async Task<List<EntityMediaLink>> LoadVariantMediaLinksAsync(Guid variantId)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _mediaLinkRepository.GetListAsync(
                l => l.EntityName == MediaEntityNames.ProductVariant && l.EntityId == variantId));
    }

    private async Task<List<EntityMediaLink>> LoadProductMediaLinksAsync(Guid productId)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _mediaLinkRepository.GetListAsync(
                l => l.EntityName == MediaEntityNames.Product && l.EntityId == productId));
    }
}
