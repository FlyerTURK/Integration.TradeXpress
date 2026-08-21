using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.ObjectMapping;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// ÜRÜN → EMTİA PROJEKSİYONU (<c>ProductToCommodityProjector</c>), YEDİ AİLE (2026-08-20)
/// — <see cref="ProductGoodProjectionTests"/>'in kardeşi.
///
/// <para><b>Bu test sınıfının varlık sebebi:</b> köprü kuralı (CLAUDE.md §6 "ANA KÖPRÜ = Product") yedi ailede
/// de çevrim ister, AMA ne taşınacağı aileye göre DEĞİŞİR ve fark sessizdir: varyantsız bir aileye varyant
/// üretmek de, varyantlı bir aileye varyant taşımamak da istisna FIRLATMAZ — kullanıcı yalnız formun yanlış
/// açıldığını görür. Üç kategori (tam varyantlı · uzantısız varyantlı · varyantsız) bu projede aynı gün ÜÇ KEZ
/// koddan yeniden çıkarıldı ve bir turda "Hurda/Vadeli seeder'ı da varyant açsın" diye yanlış öneri üretildi.
/// Kural artık bu testlerle sabitlenir.</para>
///
/// <para><b>İkinci sabitlenen kural — NE TAŞINMAZ:</b> teknik alanlar (milyem/Factor, takip birimi/katsayısı)
/// ve özel kod alanları (Marka/Model/Cins/Renk/Kategori…) üründen TÜRETİLMEZ. Bunlar "eksik doldurulmuş"
/// değildir; dolu gelmeleri kullanıcının kendi gruplama düzenini ürüne tabi kılar ve teknik gerçekliği
/// uydurur.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class ProductCommodityProjectionTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string ProductEntityName = "Product";

    // İki FARKLI 1×1 PNG şart: yükleme içerik-hash'iyle DEDUP eder (CLAUDE.md §6) — aynı baytlar TEK Media
    // kaydı üretir ve "kayıt medyası ≠ varyant medyası" iddiası anlamsızlaşırdı.
    private const string TransparentPixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private const string RedPixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private const string GreenPixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY2BocPgPAAMEAcCGLvwDAAAAAElFTkSuQmCC";

    private readonly ProductToCommodityProjector _toCommodity;
    private readonly ProductToGoodProjector _toGood;
    private readonly IMetalAppService _metals;
    private readonly IRepository<Product, Guid> _products;
    private readonly IRepository<EntityVariant, Guid> _variants;
    private readonly IRepository<EntityAttribute, Guid> _attributes;
    private readonly IRepository<EntityAttributeValue, Guid> _attributeValues;
    private readonly IRepository<EntityVariantAttributeValue, Guid> _variantValueLinks;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly IMediaAppService _media;
    private readonly IObjectMapper _objectMapper;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public ProductCommodityProjectionTests()
    {
        _toCommodity       = GetRequiredService<ProductToCommodityProjector>();
        _toGood            = GetRequiredService<ProductToGoodProjector>();
        _metals            = GetRequiredService<IMetalAppService>();
        _products          = GetRequiredService<IRepository<Product, Guid>>();
        _variants          = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _attributes        = GetRequiredService<IRepository<EntityAttribute, Guid>>();
        _attributeValues   = GetRequiredService<IRepository<EntityAttributeValue, Guid>>();
        _variantValueLinks = GetRequiredService<IRepository<EntityVariantAttributeValue, Guid>>();
        _entityMedia       = GetRequiredService<IEntityMediaAppService>();
        _media             = GetRequiredService<IMediaAppService>();
        _objectMapper      = GetRequiredService<IObjectMapper>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
    }

    // ── ① TAM VARYANTLI: Maden ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Projecting_a_product_to_a_metal_carries_attributes_variants_and_both_media_contexts()
    {
        var f = await SeedProductAsync("P2METAL");

        var projected = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToMetalAsync(f.ProductId));

        ShouldCarryIdentity(f, projected.Code, projected.Name, projected.Description);

        // Nitelik grafı TAŞINIR — kullanıcı "Renk / Kırmızı-Mavi" eksenini ikinci kez kurmasın.
        var attribute = projected.Attributes.ShouldHaveSingleItem();
        attribute.Name.ShouldBe("Renk");
        attribute.Values.Select(v => v.Value).ShouldBe(new[] { "Kırmızı", "Mavi" }, ignoreOrder: true);

        // VARYANTLAR TAŞINIR, YENİDEN ÜRETİLMEZ (kartezyeni yeniden kurmak kullanıcının elemelerini geri getirirdi).
        projected.Variants.Count.ShouldBe(2);
        projected.Variants.Select(v => v.Code)
            .ShouldBe(new[] { $"{f.Code}-V1", $"{f.Code}-V2" }, ignoreOrder: true);
        projected.Variants.Count(v => v.IsMain).ShouldBe(1);

        // MEDYA İKİ BAĞLAMDA DA bağ KOPYALAR (CLAUDE.md §6) — biri diğerinden türetilmez.
        projected.Media.ShouldHaveSingleItem().MediaId.ShouldBe(f.RecordMediaId);
        var mainVariant = projected.Variants.Single(v => v.IsMain);
        mainVariant.Media.ShouldHaveSingleItem().MediaId.ShouldBe(f.VariantMediaId);

        // İki depo AYRI: kayıt medyası varyanta, varyant medyası kayda SIZMAZ.
        projected.Media.ShouldNotContain(l => l.MediaId == f.VariantMediaId);
        mainVariant.Media.ShouldNotContain(l => l.MediaId == f.RecordMediaId);
    }

    // ── ② UZANTISIZ VARYANTLI: Mücevher ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Projecting_a_product_to_a_jewelry_carries_the_variant_graph_but_no_price()
    {
        // Mücevherde tüm varyantlar entity seviyesindeki fiyatı PAYLAŞIR; fiyat kullanıcının kararıdır ve
        // projeksiyondan geçmez (ürünün fiyatı reçeteden türetilir — maliyeti fiyat sanmak yanlış olurdu).
        var f = await SeedProductAsync("P2JEWEL");

        var projected = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToJewelryAsync(f.ProductId));

        ShouldCarryIdentity(f, projected.Code, projected.Name, projected.Description);
        projected.Attributes.ShouldHaveSingleItem();
        projected.Variants.Count.ShouldBe(2);
        projected.Media.ShouldHaveSingleItem().MediaId.ShouldBe(f.RecordMediaId);
        projected.Variants.Single(v => v.IsMain).Media
            .ShouldHaveSingleItem().MediaId.ShouldBe(f.VariantMediaId);

        projected.EntryPrice.ShouldBe(0m);
        projected.ExitPrice.ShouldBe(0m);
    }

    // ── ③ VARYANTSIZ: Taş (medya taşır) ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Projecting_a_product_to_a_stone_carries_record_media_but_never_a_variant_graph()
    {
        // Taş 2026-08-09'dan beri VARYANTSIZDIR ("her taşın parmak izi ayrıdır"). DTO'da alan DURUYOR —
        // alanın varlığı onu doldurmak için gerekçe DEĞİLDİR; doldurmak tasarımı bozardı.
        var f = await SeedProductAsync("P2STONE");

        var projected = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToStoneAsync(f.ProductId));

        ShouldCarryIdentity(f, projected.Code, projected.Name, projected.Description);
        projected.Media.ShouldHaveSingleItem().MediaId.ShouldBe(f.RecordMediaId);

        projected.Variants.ShouldBeEmpty();
        projected.Attributes.ShouldBeEmpty();
    }

    // ── ③ VARYANTSIZ: Hurda · Vadeli · Hizmet (DTO'da medya/varyant alanı YOK) ──────────────────────

    [Fact]
    public async Task Projecting_a_product_to_scrap_future_or_service_carries_identity_only()
    {
        var f = await SeedProductAsync("P2IDENT");

        var scrap   = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToScrapAsync(f.ProductId));
        var future  = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToFutureAsync(f.ProductId));
        var service = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToServiceAsync(f.ProductId));

        ShouldCarryIdentity(f, scrap.Code, scrap.Name, scrap.Description);
        ShouldCarryIdentity(f, future.Code, future.Name, future.Description);
        ShouldCarryIdentity(f, service.Code, service.Name, service.Description);

        // Üçü de yeni kayıt olarak AKTİF doğar — form "pasif" açılmaz.
        scrap.IsActive.ShouldBeTrue();
        future.IsActive.ShouldBeTrue();
        service.IsActive.ShouldBeTrue();
    }

    // ── NE TAŞINMAZ: teknik alanlar + özel kodlar ───────────────────────────────────────────────────

    [Fact]
    public async Task No_family_seeds_technical_fields_from_the_product()
    {
        // "Ürün müşteriye bakar, emtia tekniğe bakar" (Hakan, 2026-08-20): milyem/takip birimi/katsayı
        // emtianın KENDİ alanıdır. Ürün onları bilmez; seed'in doldurması "sistem biliyor" yalanı olurdu.
        var f = await SeedProductAsync("P2TEKNIK");

        var metal  = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToMetalAsync(f.ProductId));
        var scrap  = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToScrapAsync(f.ProductId));
        var future = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToFutureAsync(f.ProductId));

        metal.Factor.ShouldBe(MetalConsts.DefaultFactor);
        metal.FollowingUnitId.ShouldBeNull();
        metal.StableQuantity.ShouldBe(0m);
        metal.CostUnitId.ShouldBeNull();

        scrap.Factor.ShouldBe(ScrapConsts.DefaultFactor);
        scrap.FollowingUnitId.ShouldBeNull();

        future.FollowingFactor.ShouldBe(1m);
        future.FollowingUnitId.ShouldBeNull();
    }

    [Fact]
    public async Task No_family_seeds_special_code_fields_from_the_product()
    {
        // "Özel kod alanları üründen TÜRETİLMEZ" (Hakan, 2026-08-20): bunlar kullanıcının kendi gruplama
        // düzenidir. Ürünün müşteriye dönük "Renk" NİTELİĞİ ile emtianın "Color" ÖZEL KODU ayrı şeylerdir —
        // birini diğerine bağlamak kullanıcının stok listesini ürüne tabi kılardı.
        var f = await SeedProductAsync("P2OZELKOD");

        var good    = await WithUnitOfWorkAsync(() => _toGood.ProjectAsync(f.ProductId));
        var jewelry = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToJewelryAsync(f.ProductId));
        var stone   = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToStoneAsync(f.ProductId));

        good.Brand.ShouldBeNull();
        good.Model.ShouldBeNull();
        good.Kind.ShouldBeNull();
        good.Type.ShouldBeNull();
        good.Color.ShouldBeNull();
        good.Size.ShouldBeNull();
        good.Category.ShouldBeNull();
        good.GroupCode.ShouldBeNull();
        good.StockUnitCode.ShouldBeNull();

        jewelry.Model.ShouldBeNull();
        jewelry.Kind.ShouldBeNull();
        jewelry.Color.ShouldBeNull();
        jewelry.Category.ShouldBeNull();
        jewelry.GroupCode.ShouldBeNull();

        stone.StoneKind.ShouldBeNull();
        stone.Color.ShouldBeNull();
        stone.Cut.ShouldBeNull();
        stone.Clarity.ShouldBeNull();
        stone.Category.ShouldBeNull();
        stone.GroupCode.ShouldBeNull();
    }

    // ── Sentinel kuralı ORTAK ProductCommodityProjectionBuilder'da → varyantlı HER ailede geçerli ───

    [Fact]
    public async Task A_variantless_product_seeds_a_main_variant_coded_after_the_product_in_every_variantful_family()
    {
        // BU TEST ŞU HATAYI SABİTLER (2026-08-10, Mamül'de yaşandı): varyantsız kayıtta LoadGraphAsync boş
        // liste DÖNDÜRMEZ — ana varyantı "ANAVARYANT" sentinel'iyle üretir. O kod pazaryerine SKU olarak
        // gidebildiğinden sessiz değil PAHALI bir hatadır. Yedi aile ortak ProductCommodityProjectionBuilder'ı
        // kullandığına göre kural altı ailede de geçerli olmalı, yoksa ortaklaştırma yarım kalmış demektir.
        var f = await SeedProductAsync("P2SENTINEL", variantCount: 0, withAttributes: false);

        var metal   = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToMetalAsync(f.ProductId));
        var jewelry = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToJewelryAsync(f.ProductId));

        var metalMain = metal.Variants.ShouldHaveSingleItem();
        metalMain.IsMain.ShouldBeTrue();
        metalMain.Code.ShouldBe(f.Code);
        metalMain.Code.ShouldNotBe(EntityVariantConsts.MainVariantCode);

        var jewelryMain = jewelry.Variants.ShouldHaveSingleItem();
        jewelryMain.IsMain.ShouldBeTrue();
        jewelryMain.Code.ShouldBe(f.Code);
        jewelryMain.Code.ShouldNotBe(EntityVariantConsts.MainVariantCode);
    }

    // ── KAYIT SEVİYESİ: seed'in forma doğru gelmesi YETMEZ ──────────────────────────────────────────

    [Fact]
    public async Task Seeded_variant_customizations_and_media_survive_the_first_save_of_the_commodity()
    {
        // BU TEST ŞU HATAYI SABİTLER (2026-08-20, bağımsız denetimde yakalandı): projektör varyantın KOMBİNASYON
        // İMZASINI (CombinationKey) ve nitelik değerlerinin ClientKey'lerini düşürüyordu. Form DOĞRU açılıyordu
        // — projeksiyon testleri de yeşildi — ama kayıtta EntityVariantGraphService kartezyeni yeniden
        // materyalize edince ANA VARYANT DIŞINDAKİ her satır çözülemiyor (ResolveTargetVariant → null) ve
        // sessizce ATLANIYORDU: barkod/GTIN/MPN/stok/açıklama gitmiyor, varyant MEDYASI hiç bağlanmıyor,
        // aile-özel uzantı (MetalVariantDetail) hiç yazılmıyordu. İstisna yok, log yok — kullanıcı yalnız
        // kaydettiği bilginin yok olduğunu görüyordu. Bu yüzden test PROJEKSİYON seviyesinde bırakılamaz:
        // iddia KAYITTAN SONRA.
        var f = await SeedProductAsync("P2KAYIT");

        var seeded = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToMetalAsync(f.ProductId));

        // Formun kaydetme yolu birebir: GetDto → CreateDto (MetalGetToCreateMapper) → CreateAsync.
        var input = _objectMapper.Map<MetalGetDto, MetalCreateDto>(seeded);
        input.FollowingUnitId = f.FollowingUnitId;   // TEKNİK alan: projeksiyondan geçmez, kullanıcı formda verir

        var saved = await _metals.CreateAsync(input);

        saved.Variants.Count.ShouldBe(2);

        // Kombinasyonuna göre bul — kod otomatik türetildiği için kaynak varyant koduyla aranamaz.
        var red = saved.Variants.Single(v => v.AttributeSummary.Contains("Kırmızı"));
        var blue = saved.Variants.Single(v => v.AttributeSummary.Contains("Mavi"));

        red.Barcode.ShouldBe($"{f.Code}-BC1");
        blue.Barcode.ShouldBe(
            $"{f.Code}-BC2",
            "ANA VARYANT DIŞINDAKİ varyantın özelleştirmesi de oturmalı — imza düştüğü an bu satır sessizce atlanır.");

        // Ticari kimliklerin ÜÇÜ de oturmalı — Oem 2026-08-20'ye kadar projeksiyondan hiç geçmiyordu ve kayıp
        // sessizdi (SetTradeIdentifiers üçünü birlikte yazdığı için alan her kayıtta null'lanıyordu).
        red.Gtin.ShouldBe($"{f.Code}-GT1");
        red.Mpn.ShouldBe($"{f.Code}-MP1");
        red.Oem.ShouldBe($"{f.Code}-OE1");
        blue.Oem.ShouldBe($"{f.Code}-OE2");

        red.Media.ShouldHaveSingleItem().MediaId.ShouldBe(f.VariantMediaId);
        blue.Media.ShouldHaveSingleItem().MediaId.ShouldBe(
            f.SecondVariantMediaId,
            "Varyant medyası ancak varyant ÇÖZÜLÜRSE bağlanır (ReplaceFor uzantı geri çağrısındadır).");

        // Kayıt-geneli medya varyant koluna bağlı değildir; iki depo ayrı kaldığı burada da doğrulanır.
        saved.Media.ShouldHaveSingleItem().MediaId.ShouldBe(f.RecordMediaId);
    }

    // ── KAYITSIZ ÜRÜN: TASLAK SEED (2026-08-20) ─────────────────────────────────────────────────────

    [Fact]
    public async Task An_unsaved_product_seeds_the_commodity_form_exactly_like_a_saved_one()
    {
        // BU TEST ŞU KURALI SABİTLER: projektörün taşıdığı her şey GRAFTIR — kaydı şart koşan tek nokta
        // CombinationKey'in Guid almasıydı (ProductCommodityProjectionBuilder.BuildAsync). Kayıtsız üründe
        // zengin seed sessizce düşürülüyor, kullanıcı nitelik/varyant/görseli emtia formuna İKİNCİ KEZ
        // giriyordu.
        //
        // İDDİA KIYASLIDIR: taslak yolun çıktısı, AYNI veriyle beslenen kayıtlı yolun çıktısına eşit olmalı.
        // Tek tek alan saymak yerine kıyas seçildi çünkü asıl risk bir alanın yalnız BİR yolda taşınmasıdır
        // (Oem tam olarak böyle düşmüştü) — kıyas, gelecekte eklenen alanları da kendiliğinden kapsar.
        var f = await SeedProductAsync("PTASLAK");

        var saved = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToMetalAsync(f.ProductId));

        var draft = new ProductDraftSeedDto
        {
            Code        = saved.Code,
            Name        = saved.Name,
            Description = saved.Description,
        };
        draft.Media.AddRange(saved.Media);
        draft.Attributes.AddRange(saved.Attributes);
        draft.Variants.AddRange(saved.Variants);

        var fromDraft = await WithUnitOfWorkAsync(() => _toCommodity.ProjectDraftToMetalAsync(draft));

        fromDraft.Code.ShouldBe(saved.Code);
        fromDraft.Name.ShouldBe(saved.Name);
        fromDraft.Description.ShouldBe(saved.Description);
        fromDraft.CompanyId.ShouldBe(saved.CompanyId, "Sahiplik istemciden değil ÇALIŞILAN ŞİRKETTEN damgalanır.");

        fromDraft.Media.Select(m => m.MediaId).ShouldBe(saved.Media.Select(m => m.MediaId), ignoreOrder: true);

        fromDraft.Attributes.Select(a => a.Name).ShouldBe(saved.Attributes.Select(a => a.Name), ignoreOrder: true);
        fromDraft.Attributes.SelectMany(a => a.Values).Select(v => v.ClientKey)
            .ShouldBe(saved.Attributes.SelectMany(a => a.Values).Select(v => v.ClientKey), ignoreOrder: true,
                "Değer ClientKey'leri imzanın KAYNAĞIDIR; taslak yolda yeniden üretilirse hiçbir varyant çözülemez.");

        fromDraft.Variants.Count.ShouldBe(saved.Variants.Count);
        foreach (var expected in saved.Variants)
        {
            var actual = fromDraft.Variants.Single(v => v.Code == expected.Code);
            actual.CombinationKey.ShouldBe(expected.CombinationKey);
            actual.Barcode.ShouldBe(expected.Barcode);
            actual.Gtin.ShouldBe(expected.Gtin);
            actual.Mpn.ShouldBe(expected.Mpn);
            actual.Oem.ShouldBe(expected.Oem);
            actual.Media.Select(m => m.MediaId).ShouldBe(expected.Media.Select(m => m.MediaId), ignoreOrder: true);
        }
    }

    [Fact]
    public async Task A_draft_with_no_variants_still_seeds_a_main_variant_coded_after_the_product()
    {
        // Sentinel onarımı + "hiç varyant yok" dalı taslak yolda da KOŞAR — iki yol aynı
        // ProductCommodityProjectionBuilder'a indiği için. Taslak için ayrı bir uygulama yazılsaydı bu ince
        // kural orada unutulurdu (bu projede yaşanmış desen).
        await SeedWorkingCompanyAsync("PTASLAK-TEK");
        var draft = new ProductDraftSeedDto { Code = "PTASLAK-TEK", Name = "Taslak Ürün" };

        var projected = await WithUnitOfWorkAsync(() => _toCommodity.ProjectDraftToMetalAsync(draft));

        var main = projected.Variants.ShouldHaveSingleItem();
        main.IsMain.ShouldBeTrue();
        main.Code.ShouldBe("PTASLAK-TEK");
        main.Code.ShouldNotBe(EntityVariantConsts.MainVariantCode);
    }

    [Fact]
    public async Task A_variantless_family_ignores_the_draft_graph_instead_of_inventing_records()
    {
        // Taslak yol da AİLE SINIFLANDIRMASINA uyar: Hurda varyantsız + medyasızdır, DTO'sunda alan yoktur.
        // "Elde graf var, madem taşıyalım" demek CLAUDE.md §6'daki üç-kategori kuralını delerdi.
        var f = await SeedProductAsync("PTASLAK-HURDA");
        var saved = await WithUnitOfWorkAsync(() => _toCommodity.ProjectToMetalAsync(f.ProductId));

        var draft = new ProductDraftSeedDto { Code = saved.Code, Name = saved.Name };
        draft.Media.AddRange(saved.Media);
        draft.Attributes.AddRange(saved.Attributes);
        draft.Variants.AddRange(saved.Variants);

        var scrap = await WithUnitOfWorkAsync(() => _toCommodity.ProjectDraftToScrapAsync(draft));

        scrap.Code.ShouldBe(saved.Code);
        scrap.Name.ShouldBe(saved.Name);
    }

    [Fact]
    public async Task A_draft_without_a_working_company_is_refused_instead_of_being_stamped_with_a_guess()
    {
        // SAHİPLİK İSTEMCİDEN ALINMAZ: taslak DTO'sunda CompanyId alanı YOKTUR ve sunucu onu çalışılan
        // şirketten damgalar. Şirket yoksa fail-CLOSED — uydurma/boş bir şirketle seed üretmek, kaydı yanlış
        // şirkete açma kapısıdır ve hata ancak kayıttan sonra fark edilirdi.
        var draft = new ProductDraftSeedDto { Code = "PTASLAK-SIRKETSIZ", Name = "Sahipsiz Taslak" };

        await Should.ThrowAsync<BusinessException>(
            () => WithUnitOfWorkAsync(() => _toCommodity.ProjectDraftToMetalAsync(draft)));
    }

    [Fact]
    public async Task The_saved_path_fails_fast_on_an_unknown_product_instead_of_seeding_an_invented_record()
    {
        // BU TEST TEK FAIL-FAST KONTROLÜNÜ SABİTLER (2026-08-20 denetimi): BuildAsync'in `?? throw` satırı
        // kalkarsa metot PATLAMAZ — medya boş liste döner, varyant grafı sentinel bir ana varyant UYDURUR ve
        // sentinel onarımı ona ürünün kodunu verir. Yani hata istisna değil, UYDURMA SEED olarak çıkardı. Bu
        // yüzden kayıt yolunun fail-fast'i ayrıca sabitlenir; taslak ihtiyacı artık ayrı bir endpoint ile
        // karşılanıyor (yukarıda).
        var ex = await Should.ThrowAsync<BusinessException>(
            () => WithUnitOfWorkAsync(() => _toCommodity.ProjectToMetalAsync(Guid.NewGuid())));

        ex.Code.ShouldBe("TradeXpress:Product:NotFound");
    }

    // ── Ortak yardımcılar ───────────────────────────────────────────────────────────────────────────

    /// <summary>Ürün KAYDI OLMADAN çalışılan şirketi kurar — taslak yolu şirketi buradan damgalar.</summary>
    private async Task SeedWorkingCompanyAsync(string code)
    {
        var company = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync(code));
        _companyContext.CompanyId = company.CompanyId;
    }

    /// <summary>Yedi ailede TEKRAR EDEN kimlik iddiası — kod/ad/açıklama projeksiyondan AYNEN geçer.</summary>
    private static void ShouldCarryIdentity(ProductFixture fixture, string code, string name, string? description)
    {
        code.ShouldBe(fixture.Code);
        name.ShouldBe(fixture.Name);
        description.ShouldBe(fixture.Description);
    }

    /// <summary>Kaynak ürünün kurulum snapshot'ı — iddiaların karşılaştırdığı beklenen değerler.</summary>
    private sealed record ProductFixture(
        Guid CompanyId,
        Guid FollowingUnitId,
        Guid ProductId,
        string Code,
        string Name,
        string Description,
        Guid RecordMediaId,
        Guid VariantMediaId,
        Guid SecondVariantMediaId);

    /// <summary>
    /// Kaynak ürünü GERÇEĞE SADIK kurar: varyantlar nitelik değerlerine BAĞLANIR (Kırmızı→V1, Mavi→V2) ve her
    /// biri kendi barkodunu + kendi görselini taşır.
    ///
    /// <para><b>Bağlar neden şart:</b> bağsız kurulan bir fixture canlı veriden DAHA İYİMSERdir — varyantın
    /// kombinasyon imzası (<c>CombinationKey</c>) yalnız bağlardan doğar. Bağsız kurulumda imza her koşulda
    /// boş kalır, dolayısıyla "imza projeksiyondan geçiyor mu?" sorusu hiç sorulamaz ve imzayı düşüren bir projektör
    /// yeşil testlerle üretime çıkar (2026-08-20'de tam bu oldu).</para>
    /// </summary>
    private async Task<ProductFixture> SeedProductAsync(string code, int variantCount = 2, bool withAttributes = true)
    {
        var company = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync(code));
        _companyContext.CompanyId = company.CompanyId;

        var recordMediaId = await UploadAsync($"{code}-kayit.png", TransparentPixelPng);
        var variantMediaId = await UploadAsync($"{code}-varyant1.png", RedPixelPng);
        var secondVariantMediaId = await UploadAsync($"{code}-varyant2.png", GreenPixelPng);
        new[] { recordMediaId, variantMediaId, secondVariantMediaId }.Distinct().Count()
            .ShouldBe(3, "Üç görsel FARKLI olmalı; aksi halde içerik-hash dedup'ı tek kayda indirir.");

        var name = $"{code} Ürünü";
        var description = $"{code} açıklaması";

        var productId = await WithUnitOfWorkAsync(async () =>
        {
            var product = new Product(company.CompanyId, code, name);
            product.SetDescription(description);
            await _products.InsertAsync(product, autoSave: true);

            var attributeId = Guid.Empty;
            var valueIds = new List<Guid>();
            if (withAttributes)
            {
                var attribute = new EntityAttribute(company.CompanyId, ProductEntityName, product.Id, "Renk");
                await _attributes.InsertAsync(attribute, autoSave: true);
                attributeId = attribute.Id;

                foreach (var (value, order) in new[] { ("Kırmızı", 0), ("Mavi", 1) })
                {
                    var row = new EntityAttributeValue(company.CompanyId, attribute.Id, value, order);
                    await _attributeValues.InsertAsync(row, autoSave: true);
                    valueIds.Add(row.Id);
                }
            }

            for (var i = 1; i <= variantCount; i++)
            {
                var variant = new EntityVariant(
                    company.CompanyId, ProductEntityName, product.Id, $"{code}-V{i}", $"{code} Varyant {i}", isMain: i == 1);
                variant.SetBarcode($"{code}-BC{i}");

                // ÜÇ TİCARİ KİMLİK DE dolu seed'lenir: projektör bunları tek tek taşır ve biri düşerse eksiklik
                // sessizdir (kayıt yolu üçünü BİRLİKTE yazar → taşınmayan alan her seferinde null'lanır).
                variant.SetTradeIdentifiers($"{code}-GT{i}", $"{code}-MP{i}", $"{code}-OE{i}");
                await _variants.InsertAsync(variant, autoSave: true);

                // Varyant KOMBİNASYONDAN doğar: i. varyant i. nitelik değerine bağlanır.
                if (valueIds.Count >= i)
                {
                    await _variantValueLinks.InsertAsync(
                        new EntityVariantAttributeValue(company.CompanyId, variant.Id, attributeId, valueIds[i - 1]),
                        autoSave: true);
                }

                await _entityMedia.ReplaceForAsync(
                    MediaEntityNames.ProductVariant, variant.Id, company.CompanyId,
                    LinksTo(i == 1 ? variantMediaId : secondVariantMediaId));
            }

            await _entityMedia.ReplaceForAsync(
                MediaEntityNames.Product, product.Id, company.CompanyId, LinksTo(recordMediaId));

            return product.Id;
        });

        return new ProductFixture(
            company.CompanyId, company.GumUnitId, productId, code, name, description,
            recordMediaId, variantMediaId, secondVariantMediaId);
    }

    private async Task<Guid> UploadAsync(string fileName, string base64Png)
    {
        var dto = await WithUnitOfWorkAsync(() => _media.UploadAsync(new MediaUploadDto
        {
            FileName = fileName,
            Content = Convert.FromBase64String(base64Png),
        }));

        return dto.Id;
    }

    private static List<EntityMediaLinkEditDto> LinksTo(Guid mediaId)
    {
        return new List<EntityMediaLinkEditDto>
        {
            new EntityMediaLinkEditDto
            {
                MediaId = mediaId,
                IsActive = true,
                IsDefault = true,
                DisplayOrder = 0,
            },
        };
    }
}
