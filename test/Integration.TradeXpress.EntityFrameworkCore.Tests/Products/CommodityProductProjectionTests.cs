using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// EMTİA → ÜRÜN PROJEKSİYONUNUN (<c>CommodityToProductProjector</c>) SÖZLEŞMESİ — yedi ailenin tamamı
/// (2026-08-20 Hakan talimatı: köprü Mamül'de olduğu kadar diğer altı ailede de olgun olmalı;
/// "ana köprü = Product").
///
/// <para><b>Neden app service üzerinden ve projektörü doğrudan çağırarak DEĞİL:</b> ailenin kaydını okuyup
/// hangi alanların projektöre verileceğine karar veren yer app service'tir. Projektörü doğrudan çağıran bir
/// test, "kod/ad/açıklama taşınıyor" derken aslında kendi verdiği değerleri doğrulardı — asıl sorulan
/// (<c>Metal.Description</c> gerçekten okunuyor mu) hiç sınanmazdı.</para>
///
/// <para><b>Sabitlenen dört şey:</b> ① aile-kategorisi tablosunun (kim varyant taşır) TEK kaynakta kalması —
/// bu ayrım koddan defalarca yeniden çıkarıldı ve bir turda YANLIŞ gruplandı; ② varyantsız ailede ürünün TEK
/// ana varyantla ve KAYDIN koduyla doğması — "ANAVARYANT" sentinel'i pazaryerine SKU olarak gidebildiği için
/// sessiz değil PAHALI bir hatadır; ③ varyantlı ailede varyant grafının ve İKİ medya bağlamının taşınması;
/// ④ teknik ve özel-kod alanlarının ürüne SIZMAMASI — bugün sızmıyor çünkü ürün DTO'sunda karşılıkları yok,
/// ve bu test o yokluğu bir KARAR olarak sabitler.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class CommodityProductProjectionTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string SeededDescription = "Kopruden tasinan aciklama";

    private const string TransparentPixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private const string RedPixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private readonly IMetalAppService _metalService;
    private readonly IScrapAppService _scrapService;
    private readonly IFutureAppService _futureService;
    private readonly IJewelryAppService _jewelryService;
    private readonly IStoneAppService _stoneService;
    private readonly IServiceAppService _serviceService;
    private readonly IGoodAppService _goodService;

    private readonly IRepository<Metal, Guid> _metals;
    private readonly IRepository<Scrap, Guid> _scraps;
    private readonly IRepository<Future, Guid> _futures;
    private readonly IRepository<Jewelry, Guid> _jewelries;
    private readonly IRepository<Stone, Guid> _stones;
    private readonly IRepository<Service, Guid> _services;
    private readonly IRepository<Good, Guid> _goods;
    private readonly IRepository<EntityVariant, Guid> _variants;

    private readonly IEntityMediaAppService _entityMedia;
    private readonly IMediaAppService _mediaService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    /// <summary>Takip birimi — maden/hurda/vadeli ctor'ları için ZORUNLU. Projeksiyona GİRMEYEN teknik bir
    /// alandır; burada yalnız kaydın kurulabilmesi için taşınır.</summary>
    private Guid _followingUnitId;

    public CommodityProductProjectionTests()
    {
        _metalService   = GetRequiredService<IMetalAppService>();
        _scrapService   = GetRequiredService<IScrapAppService>();
        _futureService  = GetRequiredService<IFutureAppService>();
        _jewelryService = GetRequiredService<IJewelryAppService>();
        _stoneService   = GetRequiredService<IStoneAppService>();
        _serviceService = GetRequiredService<IServiceAppService>();
        _goodService    = GetRequiredService<IGoodAppService>();

        _metals    = GetRequiredService<IRepository<Metal, Guid>>();
        _scraps    = GetRequiredService<IRepository<Scrap, Guid>>();
        _futures   = GetRequiredService<IRepository<Future, Guid>>();
        _jewelries = GetRequiredService<IRepository<Jewelry, Guid>>();
        _stones    = GetRequiredService<IRepository<Stone, Guid>>();
        _services  = GetRequiredService<IRepository<Service, Guid>>();
        _goods     = GetRequiredService<IRepository<Good, Guid>>();
        _variants  = GetRequiredService<IRepository<EntityVariant, Guid>>();

        _entityMedia    = GetRequiredService<IEntityMediaAppService>();
        _mediaService   = GetRequiredService<IMediaAppService>();
        _seeder         = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    // ── Aile-kategorisi tablosu (SSOT) ──────────────────────────────────────────────────────────────

    /// <summary>Yedi ailenin HEPSİ tabloda olmalı ve tablo tanımadığı aileye varsayılan ÜRETMEMELİ.
    /// Varsayılan üretmek, sekizinci bir aileyi sessizce "varyantsız + medyasız" tarafa düşürürdü.</summary>
    [Fact]
    public void Every_commodity_family_declares_a_projection_shape()
    {
        CommodityProjectionShapes.Families.Count.ShouldBe(7);

        foreach (var family in CommodityProjectionShapes.Families)
        {
            CommodityProjectionShapes.Of(family).EntityName
                .ShouldNotBeNullOrWhiteSpace($"{family}: sahip bağlam adı boş olamaz.");
        }

        // Emtia OLMAYAN bir süreç türü projeksiyona giremez — sessizce boş bir ürün üretmek yerine fail-fast.
        Should.Throw<BusinessException>(() => CommodityProjectionShapes.Of(ProcessType.Cash));
    }

    /// <summary>③ VARYANTSIZ aileler (Hurda · Vadeli · Taş · Hizmet) varyant grafı BEYAN ETMEZ.
    /// <para>Bu bir eksiklik değil TASARIMDIR ("vadeli varyant barındırmaz" · "her taşın parmak izi ayrıdır" ·
    /// stoklanmayan hizmetin varyantı olmaz). Beyan sızarsa projektör o ailelerde varyant üretmeye başlar.</para></summary>
    [Theory]
    [InlineData(ProcessType.Scrap)]
    [InlineData(ProcessType.Future)]
    [InlineData(ProcessType.Stone)]
    [InlineData(ProcessType.Service)]
    public void Variantless_families_declare_no_variant_graph(ProcessType family)
    {
        var shape = CommodityProjectionShapes.Of(family);

        shape.CarriesVariantGraph.ShouldBeFalse($"{family} VARYANTSIZ bir ailedir.");
        shape.VariantMediaContext.ShouldBeNull($"{family}'de varyant kaydı yok; bağlanacak varyant medyası da yok.");
    }

    /// <summary>① ve ② varyant TAŞIYAN aileler İKİ medya bağlamını da beyan etmeli (CLAUDE.md §6 "her medya
    /// tipi iki bağlamı da taşır"): genel görsel kayıtta, farklılık görselleri varyantta.</summary>
    [Theory]
    [InlineData(ProcessType.Metal)]
    [InlineData(ProcessType.Jewelry)]
    [InlineData(ProcessType.Good)]
    public void Variant_carrying_families_declare_both_media_contexts(ProcessType family)
    {
        var shape = CommodityProjectionShapes.Of(family);

        shape.CarriesVariantGraph.ShouldBeTrue();
        shape.RecordMediaContext.ShouldNotBeNullOrWhiteSpace();
        shape.VariantMediaContext.ShouldNotBeNullOrWhiteSpace();
        shape.VariantMediaContext.ShouldNotBe(shape.RecordMediaContext);
    }

    /// <summary>Projeksiyonun GERİ yönü YEDİ app service sözleşmesinde de açık olmalı — biri eksik kalırsa o
    /// ailede projeksiyon sessizce yarım kalır (derleme hatası vermez, yalnız hiç yazılmamış olur).</summary>
    [Fact]
    public void Every_commodity_app_service_exposes_the_product_projection()
    {
        var contracts = new[]
        {
            typeof(IMetalAppService), typeof(IScrapAppService), typeof(IFutureAppService),
            typeof(IJewelryAppService), typeof(IStoneAppService), typeof(IGoodAppService),
            typeof(IServiceAppService),
        };

        foreach (var contract in contracts)
        {
            var method = contract.GetMethod("ProjectToProductAsync");
            method.ShouldNotBeNull($"{contract.Name}: emtia → ürün köprüsünün geri yönü eksik.");
            method!.ReturnType.ShouldBe(typeof(Task<ProductGetDto>));
        }
    }

    // ── Kimlik: yedi ailede de kod · ad · açıklama ──────────────────────────────────────────────────

    /// <summary>Projeksiyonun taşıdığı ÜÇ kimlik alanı her ailede aynı: kod · ad · açıklama. Ürün AKTİF doğar
    /// ama KATEGORİSİZ — kategori emtiadan türetilemez (emtianın "Kategori" özel kodu ürün kategorisi
    /// DEĞİLDİR) ve ürün formunun kademeli kilidinde ilk adım zaten odur.</summary>
    [Theory]
    [InlineData(ProcessType.Metal)]
    [InlineData(ProcessType.Scrap)]
    [InlineData(ProcessType.Future)]
    [InlineData(ProcessType.Jewelry)]
    [InlineData(ProcessType.Stone)]
    [InlineData(ProcessType.Service)]
    [InlineData(ProcessType.Good)]
    public async Task Projecting_a_commodity_carries_code_name_and_description(ProcessType family)
    {
        var companyId = await NewCompanyAsync($"K{(int)family:00}");
        var code = $"KML-{(int)family:00}";
        var commodityId = await SeedCommodityAsync(family, companyId, code);

        var projected = await ProjectAsync(family, commodityId);

        projected.Code.ShouldBe(code);
        projected.Name.ShouldBe($"{code} Kaydi");
        projected.Description.ShouldBe(SeededDescription);
        projected.IsActive.ShouldBeTrue();
        projected.ProductCategoryId.ShouldBeNull("Kategori emtiadan TÜRETİLEMEZ; kullanıcı ürün formunda seçer.");
    }

    /// <summary>KDV yalnız KARŞILIĞI OLAN ailede taşınır. Bugün bu yalnız Mamül'dür; kalan altı ailede
    /// emtiada KDV alanı YOKTUR ve uydurulmuş bir oran kullanıcıya "sistem biliyor" izlenimi verirdi.</summary>
    [Theory]
    [InlineData(ProcessType.Metal)]
    [InlineData(ProcessType.Scrap)]
    [InlineData(ProcessType.Future)]
    [InlineData(ProcessType.Jewelry)]
    [InlineData(ProcessType.Stone)]
    [InlineData(ProcessType.Service)]
    public async Task A_family_without_a_vat_counterpart_leaves_the_product_vat_empty(ProcessType family)
    {
        var companyId = await NewCompanyAsync($"V{(int)family:00}");
        var code = $"KDV-{(int)family:00}";
        var commodityId = await SeedCommodityAsync(family, companyId, code);

        var projected = await ProjectAsync(family, commodityId);

        projected.VatRate.ShouldBeNull($"{family} ailesinde KDV alanı yok; uydurulmamalı.");
    }

    /// <summary>Mamülde KDV VARDIR (satış oranı) ve taşınır — projeksiyonun "varsa karşılığı" kuralının pozitif
    /// tarafı. Değeri değil VARLIĞI sabitlenir: oran mamül formunun kararıdır.</summary>
    [Fact]
    public async Task A_good_carries_its_vat_rate_to_the_product()
    {
        var companyId = await NewCompanyAsync("KDVG");
        var goodId = await SeedGoodAsync(companyId, "KDV-MAM");

        var projected = await ProjectAsync(ProcessType.Good, goodId);

        projected.VatRate.ShouldNotBeNull("Mamülün KDV oranı ürüne taşınmalı (kanal kayıtları onu devralır).");
    }

    // ── Varyant TAŞIYAN aileler ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Projecting_a_metal_carries_its_variants_and_both_media_contexts()
    {
        var companyId = await NewCompanyAsync("M2P");
        var metalId = await SeedCommodityAsync(ProcessType.Metal, companyId, "MDN-M2P");

        var recordMedia = await UploadAsync("kayit.png", TransparentPixelPng);
        var variantMedia = await UploadAsync("varyant.png", RedPixelPng);
        variantMedia.ShouldNotBe(recordMedia, "İki görsel FARKLI olmalı; aynı baytlar ContentHash ile tek kayda iner.");

        var variantIds = await SeedVariantsAsync(companyId, MediaEntityNames.Metal, metalId, "MDN-M2P", count: 2);
        await LinkMediaAsync(MediaEntityNames.Metal, metalId, companyId, recordMedia);
        await LinkMediaAsync(MediaEntityNames.MetalVariant, variantIds[0], companyId, variantMedia);

        var projected = await ProjectAsync(ProcessType.Metal, metalId);

        // VARYANTLAR TAŞINIR, yeniden ÜRETİLMEZ — kartezyeni kurmak kullanıcının elemelerini geri getirirdi.
        projected.Variants.Count.ShouldBe(2);
        projected.Variants.Select(v => v.Code).ShouldBe(new[] { "MDN-M2P-V1", "MDN-M2P-V2" }, ignoreOrder: true);
        projected.Variants.Count(v => v.IsMain).ShouldBe(1);

        // İKİ MEDYA BAĞLAMI DA taşınır ve KARIŞMAZ (kayıt geneli → kayıt geneli, varyant → varyant).
        projected.Media.ShouldHaveSingleItem().MediaId.ShouldBe(recordMedia);
        projected.Media.ShouldNotContain(l => l.MediaId == variantMedia);
        projected.Variants.Single(v => v.Code == "MDN-M2P-V1").Media
            .ShouldHaveSingleItem().MediaId.ShouldBe(variantMedia);
        projected.Variants.Single(v => v.Code == "MDN-M2P-V2").Media.ShouldBeEmpty();
    }

    /// <summary>Mücevher ② UZANTISIZ varyantlı ailedir: varyant grafı taşınır. Fiyat mücevherde ENTITY
    /// seviyesindedir ve projeksiyondan GEÇMEZ — üründe fiyat reçeteden türetilir; giriş fiyatını satış fiyatına
    /// yazmak maliyeti fiyat sanmak olurdu.</summary>
    [Fact]
    public async Task Projecting_a_jewelry_carries_its_variant_graph_without_price_or_recipe()
    {
        var companyId = await NewCompanyAsync("J2P");
        var jewelryId = await SeedCommodityAsync(ProcessType.Jewelry, companyId, "MCV-J2P");
        await SeedVariantsAsync(companyId, MediaEntityNames.Jewelry, jewelryId, "MCV-J2P", count: 3);

        var projected = await ProjectAsync(ProcessType.Jewelry, jewelryId);

        projected.Variants.Count.ShouldBe(3);
        projected.Variants.ShouldAllBe(v => v.SalePrice == null);
        projected.Variants.ShouldAllBe(v => v.RecipeLines.Count == 0);
    }

    // ── VARYANTSIZ aileler ──────────────────────────────────────────────────────────────────────────

    /// <summary>Ürün tarafı varyantsız OLAMAZ (SKU'yu varyant taşır) → TEK ana varyant. Kodu "ANAVARYANT"
    /// sentinel'i DEĞİL, kaydın kendi kodudur: tek varyant bir ayrım değildir ve o sentinel pazaryerine SKU
    /// olarak giderdi (2026-08-06 kararı).</summary>
    [Theory]
    [InlineData(ProcessType.Scrap)]
    [InlineData(ProcessType.Future)]
    [InlineData(ProcessType.Stone)]
    [InlineData(ProcessType.Service)]
    public async Task A_variantless_commodity_projects_a_single_main_variant_coded_after_the_record(ProcessType family)
    {
        var companyId = await NewCompanyAsync($"T{(int)family:00}");
        var code = $"TEK-{(int)family:00}";
        var commodityId = await SeedCommodityAsync(family, companyId, code);

        var projected = await ProjectAsync(family, commodityId);

        var main = projected.Variants.ShouldHaveSingleItem();
        main.IsMain.ShouldBeTrue();
        main.Code.ShouldBe(code);
        main.Code.ShouldNotBe(EntityVariantConsts.MainVariantCode);
        projected.Attributes.ShouldBeEmpty($"{family} varyantsızdır; nitelik ekseni de olamaz.");
    }

    /// <summary>Taş VARYANTSIZDIR ama kayıt-geneli MEDYA taşır — ikisi AYRI sorulardır ve projektör onları
    /// birbirine bağlamaz (varyantsız diye görseli düşürmek "taşın fotoğrafı yok" demek olurdu).</summary>
    [Fact]
    public async Task Projecting_a_stone_carries_record_media_but_stays_variantless()
    {
        var companyId = await NewCompanyAsync("S2P");
        var stoneId = await SeedCommodityAsync(ProcessType.Stone, companyId, "TAS-S2P");
        var recordMedia = await UploadAsync("tas.png", TransparentPixelPng);
        await LinkMediaAsync(MediaEntityNames.Stone, stoneId, companyId, recordMedia);

        var projected = await ProjectAsync(ProcessType.Stone, stoneId);

        projected.Media.ShouldHaveSingleItem().MediaId.ShouldBe(recordMedia);
        projected.Variants.ShouldHaveSingleItem().Code.ShouldBe("TAS-S2P");
    }

    /// <summary>Hurda/Vadeli/Hizmet'te medya bağlamı YOKTUR — projektör o kolu hiç çalıştırmaz ve ürün görselsiz
    /// doğar (kullanıcı ürün formunda ekler).</summary>
    [Fact]
    public async Task A_media_less_family_projects_a_product_without_media()
    {
        var companyId = await NewCompanyAsync("H2P");
        var scrapId = await SeedCommodityAsync(ProcessType.Scrap, companyId, "HRD-H2P");

        var projected = await ProjectAsync(ProcessType.Scrap, scrapId);

        projected.Media.ShouldBeEmpty();
    }

    // ── Taşınmayanlar ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// TEKNİK ve ÖZEL KOD alanlarının ürün DTO'sunda KARŞILIĞI YOKTUR — ve olmaması bir KARARDIR.
    ///
    /// <para>"Ürün müşteriye bakar, emtia tekniğe bakar": milyem/faktör, takip birimi, giriş fiyatı ve stok
    /// birimi emtianın alanlarıdır ve ürüne yalnız reçete satırı üzerinden gelir. Özel kodlar (Marka/Model/
    /// Cins/Tür/Renk/Ölçü/Kategori/Grup) kullanıcının gruplama düzenidir ve üründen türetilmez. Bu test,
    /// birisi "kolaylık olsun" diye ürüne bir <c>Factor</c> alanı eklediğinde kırılır — projektörü değil KURALI
    /// korur.</para>
    /// </summary>
    [Fact]
    public void Technical_and_special_code_fields_have_no_counterpart_on_the_product()
    {
        var forbidden = new[]
        {
            "Factor", "FollowingFactor", "FollowingUnitId", "StableQuantity", "IsQuantity",
            "EntryPrice", "ExitPrice", "EntryPriceUnitId", "ExitPriceUnitId",
            "PriceByQuantity", "PriceTypeChange", "FactorChange",
            "Brand", "Model", "Kind", "Type", "Color", "Size", "GroupCode",
            "StoneKind", "StoneType", "Cut", "Clarity", "Sieve",
        };

        var declared = typeof(ProductGetDto).GetProperties().Select(p => p.Name).ToHashSet();

        foreach (var name in forbidden)
        {
            declared.ShouldNotContain(
                name,
                $"'{name}' emtianın alanıdır; ürüne eklenirse köprü onu taşımaya zorlanır.");
        }
    }

    /// <summary>Projeksiyon KAYDETMEZ — ne emtia tarafına varyant yazar ne ürün açar. Sessizce kayıt açmak
    /// "sınıflandırma manueldir, yazılım tahmin etmez" kuralını delerdi.</summary>
    [Fact]
    public async Task Projection_persists_nothing()
    {
        var companyId = await NewCompanyAsync("P0P");
        var futureId = await SeedCommodityAsync(ProcessType.Future, companyId, "VDL-P0P");

        var before = await CountVariantsAsync();
        await ProjectAsync(ProcessType.Future, futureId);
        var after = await CountVariantsAsync();

        after.ShouldBe(before, "Projeksiyon persistsizdir; ürünün ana varyantı DB'ye yazılmamalı.");
    }

    // ── Projeksiyon çağrısı ─────────────────────────────────────────────────────────────────────────

    private Task<ProductGetDto> ProjectAsync(ProcessType family, Guid commodityId)
    {
        return WithUnitOfWorkAsync(() =>
        {
            switch (family)
            {
                case ProcessType.Metal:
                    return _metalService.ProjectToProductAsync(commodityId);

                case ProcessType.Scrap:
                    return _scrapService.ProjectToProductAsync(commodityId);

                case ProcessType.Future:
                    return _futureService.ProjectToProductAsync(commodityId);

                case ProcessType.Jewelry:
                    return _jewelryService.ProjectToProductAsync(commodityId);

                case ProcessType.Stone:
                    return _stoneService.ProjectToProductAsync(commodityId);

                case ProcessType.Service:
                    return _serviceService.ProjectToProductAsync(commodityId);

                case ProcessType.Good:
                    return _goodService.ProjectToProductAsync(commodityId);
            }

            throw new ArgumentOutOfRangeException(nameof(family), family, "Emtia ailesi bekleniyordu.");
        });
    }

    // ── Kurulum yardımcıları ────────────────────────────────────────────────────────────────────────

    private async Task<Guid> NewCompanyAsync(string prefix)
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync(prefix));
        _companyContext.CompanyId = data.CompanyId;
        _followingUnitId = data.HasUnitId;
        return data.CompanyId;
    }

    private Task<Guid> SeedCommodityAsync(ProcessType family, Guid companyId, string code)
    {
        switch (family)
        {
            case ProcessType.Metal:
                return SeedMetalAsync(companyId, code);

            case ProcessType.Scrap:
                return SeedScrapAsync(companyId, code);

            case ProcessType.Future:
                return SeedFutureAsync(companyId, code);

            case ProcessType.Jewelry:
                return SeedJewelryAsync(companyId, code);

            case ProcessType.Stone:
                return SeedStoneAsync(companyId, code);

            case ProcessType.Service:
                return SeedServiceAsync(companyId, code);

            case ProcessType.Good:
                return SeedGoodAsync(companyId, code);
        }

        throw new ArgumentOutOfRangeException(nameof(family), family, "Emtia ailesi bekleniyordu.");
    }

    private Task<Guid> SeedMetalAsync(Guid companyId, string code)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var metal = new Metal(code, $"{code} Kaydi", _followingUnitId, companyId);
            metal.SetDescription(SeededDescription);
            await _metals.InsertAsync(metal, autoSave: true);
            return metal.Id;
        });
    }

    private Task<Guid> SeedScrapAsync(Guid companyId, string code)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var scrap = new Scrap(code, $"{code} Kaydi", _followingUnitId, companyId);
            scrap.SetDescription(SeededDescription);
            await _scraps.InsertAsync(scrap, autoSave: true);
            return scrap.Id;
        });
    }

    private Task<Guid> SeedFutureAsync(Guid companyId, string code)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var future = new Future(code, $"{code} Kaydi", _followingUnitId, companyId);
            future.SetDescription(SeededDescription);
            await _futures.InsertAsync(future, autoSave: true);
            return future.Id;
        });
    }

    private Task<Guid> SeedJewelryAsync(Guid companyId, string code)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var jewelry = new Jewelry(code, $"{code} Kaydi", companyId);
            jewelry.SetDescription(SeededDescription);
            await _jewelries.InsertAsync(jewelry, autoSave: true);
            return jewelry.Id;
        });
    }

    private Task<Guid> SeedStoneAsync(Guid companyId, string code)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var stone = new Stone(code, $"{code} Kaydi", companyId);
            stone.SetDescription(SeededDescription);
            await _stones.InsertAsync(stone, autoSave: true);
            return stone.Id;
        });
    }

    private Task<Guid> SeedServiceAsync(Guid companyId, string code)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var service = new Service(code, $"{code} Kaydi", companyId);
            service.SetDescription(SeededDescription);
            await _services.InsertAsync(service, autoSave: true);
            return service.Id;
        });
    }

    private Task<Guid> SeedGoodAsync(Guid companyId, string code)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var good = new Good(code, $"{code} Kaydi", companyId);
            good.SetDescription(SeededDescription);
            await _goods.InsertAsync(good, autoSave: true);
            return good.Id;
        });
    }

    private Task<List<Guid>> SeedVariantsAsync(Guid companyId, string entityName, Guid entityId, string codePrefix, int count)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var ids = new List<Guid>();
            for (var i = 1; i <= count; i++)
            {
                var variant = new EntityVariant(
                    companyId, entityName, entityId, $"{codePrefix}-V{i}", $"{codePrefix} Varyant {i}", isMain: i == 1);
                await _variants.InsertAsync(variant, autoSave: true);
                ids.Add(variant.Id);
            }

            return ids;
        });
    }

    private async Task<Guid> UploadAsync(string fileName, string base64Png)
    {
        var dto = await WithUnitOfWorkAsync(async () => await _mediaService.UploadAsync(new MediaUploadDto
        {
            FileName = fileName,
            Content = Convert.FromBase64String(base64Png),
        }));

        return dto.Id;
    }

    private Task LinkMediaAsync(string entityName, Guid entityId, Guid companyId, Guid mediaId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            await _entityMedia.ReplaceForAsync(entityName, entityId, companyId, new List<EntityMediaLinkEditDto>
            {
                new EntityMediaLinkEditDto
                {
                    MediaId = mediaId,
                    IsActive = true,
                    IsDefault = true,
                    DisplayOrder = 0,
                },
            });

            return true;
        });
    }

    private Task<int> CountVariantsAsync()
    {
        return WithUnitOfWorkAsync(async () => (int)await _variants.GetCountAsync());
    }
}
