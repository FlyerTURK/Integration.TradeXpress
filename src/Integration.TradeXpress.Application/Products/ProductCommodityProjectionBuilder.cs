using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Products;

/// <summary>
/// ÜRÜN → EMTİA köprüsünün taşıyacağı ŞEKİL — aile başına TEK satırlık beyan.
///
/// <para><b>Neden enum:</b> "hangi aile ne taşır" kuralı (CLAUDE.md §6 "emtia aileleri varyant açısından ÜÇ
/// kategoridir") yedi projektöre kopyalandığında biri sessizce diğerlerinden sapar — bu projede tam bu desen
/// defalarca yaşandı. Şekil bir PARAMETRE olunca kural <c>ProductCommodityProjectionBuilder</c>'ın içinde TEK
/// yerde yaşar, aile yalnız hangi kategoride olduğunu söyler.</para>
///
/// <para><b>Bu enum ELLE SEÇİLMEZ, TÜRETİLİR (2026-08-20):</b> hangi ailenin hangi şekli taşıdığı TEK yerde,
/// <c>CommodityProjectionShapes</c> tablosunda yazılıdır; çağıran şekli <c>ForwardShapeOf(ProcessType)</c>
/// ile sorar. Önceden aynı üç-kategori sınıflandırması ileri ve geri yönde AYRI AYRI beyan ediliyordu —
/// tutarlıydılar ama sekizinci bir aile eklendiğinde birinin güncellenip diğerinin unutulması bu projede
/// defalarca yaşanan desendir. Enum bu yüzden bir SEÇENEK listesi değil, tablonun sonucudur.</para>
/// </summary>
public enum ProductProjectionShape
{
    /// <summary>Yalnız kimlik: kod · ad · açıklama. DTO'sunda medya/nitelik/varyant alanı OLMAYAN aileler
    /// (Hurda · Vadeli · Hizmet). <b>Alanı olmayana veri UYDURULMAZ.</b></summary>
    Identity = 0,

    /// <summary>Kimlik + KAYIT-GENELİ medya; nitelik ve varyant grafı TAŞINMAZ. Varyantsız ama medya taşıyan
    /// aile (Taş): "her taşın parmak izi ayrıdır" — varyant üretmek eksikliği değil TASARIMI bozardı.</summary>
    RecordMedia = 1,

    /// <summary>Kimlik + kayıt-geneli medya + nitelik grafı + varyant grafı (varyant medyasıyla birlikte).
    /// Tam varyantlı (Maden · Mamül) ve uzantısız varyantlı (Mücevher) aileler.</summary>
    FullGraph = 2,
}

/// <summary>
/// KÖPRÜDEN GEÇEN ALANLAR — aynı zamanda "ne taşınır / ne taşınmaz" kuralının makine-okunur hâli.
///
/// <para>Burada OLMAYAN bir alan köprüden geçmez: teknik alanlar (milyem/Factor, takip birimi, giriş fiyatı,
/// stok birimi, ÖTV/stopaj, marj) emtianın KENDİ alanlarıdır ve kullanıcı emtia formunda verir; özel kod
/// alanları (Marka/Model/Cins/Tür/Renk/Beden/Kategori/Grup) kullanıcının gruplama düzenidir ve üründen
/// TÜRETİLMEZ (CLAUDE.md §6 "ürün müşteriye bakar, emtia tekniğe bakar" + "özel kod alanları üründen
/// türetilmez"). Bu tipi genişletmek o kuralı gevşetmek demektir — önce kuralı konuş.</para>
///
/// <para><b>DOKÜMAN ve NOT köprüde YOK — ama eksiklik köprüde DEĞİL</b> (2026-08-20 denetimi): dört emtia
/// ailesinin (Maden · Mücevher · Taş · Mamül) formunda doküman/not sekmesi var, <c>ÜRÜNDE HİÇ YOK</c> — ne
/// <c>ProductGetDto</c>'da alan, ne formda panel, ne kayıtta <c>ReplaceForAsync</c> çağrısı. Yani köprü
/// taşınabilir bir şeyi düşürmüyor; kaynak tarafta hiç doğmamış. Bu tipe alan eklemek sorunu ÇÖZMEZ, yalnız
/// boş liste taşır. Ürüne doküman/not yeteneği açmak ayrı bir İŞ kararıdır (yeni sekme + kayıt yolu);
/// açıldığı gün buraya iki alan eklemek yeterli olacak. <b>Denetimde "köprü doküman taşımıyor" diye yeniden
/// gündeme gelmesin diye buraya yazıldı.</b></para>
/// </summary>
public sealed class ProductProjectionSeed
{
    public Guid ProductId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public Guid CompanyId { get; init; }

    /// <summary>Ürünün KDV oranı — yalnız KARŞILIĞI OLAN ailede (bugün Mamül) tüketilir. <c>null</c> = ürün
    /// beyan etmemiş; tüketen taraf kendi varsayılanına düşer (uydurma oran BURADA üretilmez).</summary>
    public int? VatRate { get; init; }

    /// <summary>KAYIT-GENELİ medya bağları (<c>Product</c> bağlamı). Bağlar <c>MediaId</c> ile kopyalanır,
    /// dosya YENİDEN YÜKLENMEZ — medya içeriği değişmezdir (aynı ContentHash ikinci kayıt açmaz).</summary>
    public List<EntityMediaLinkEditDto> Media { get; } = new();

    public List<EntityAttributeGraphDto> Attributes { get; } = new();

    /// <summary>Varyant grafı — taban <c>EntityVariantGraphDto</c> (aile-özel uzantı alanı taşımaz). Aileye özel DTO'ya
    /// <see cref="VariantsAs{TVariant}"/> ile çevrilir.</summary>
    public List<EntityVariantGraphDto> Variants { get; } = new();

    /// <summary>
    /// Varyant grafını ailenin KENDİ varyant DTO'suna çevirir (ör. <c>GoodVariantGraphDto</c>,
    /// <c>MetalVariantGraphDto</c>) — yalnız <c>EntityVariantGraphDto</c>'nun core alanları kopyalanır.
    ///
    /// <para><b>Aile-özel varyant alanları bilinçle BOŞ kalır:</b> Mamül'ün varyant fiyatı, Maden'in varyant
    /// işçiliği teknik/ticari kararlardır; ürün onları bilmez ve uydurulmuş bir değer kullanıcıya "sistem
    /// biliyor" izlenimi verirdi.</para>
    ///
    /// <para><b><see cref="EntityVariantGraphDto.CombinationKey"/> de KOPYALANIR</b> — o alan görüntü değil
    /// KİMLİKtir; düşürülürse kayıtta varyant özelleştirmeleri sessizce kaybolur (bkz.
    /// <see cref="ProductCommodityProjectionBuilder"/> "kombinasyon kimliği" notu).</para>
    /// </summary>
    public List<TVariant> VariantsAs<TVariant>()
        where TVariant : EntityVariantGraphDto, new()
    {
        var projected = new List<TVariant>();
        foreach (var v in Variants)
        {
            projected.Add(new TVariant
            {
                IsMain           = v.IsMain,
                Code             = v.Code,
                Name             = v.Name,
                Description      = v.Description,
                IsActive         = v.IsActive,
                Barcode          = v.Barcode,
                Gtin             = v.Gtin,
                Mpn              = v.Mpn,
                Oem              = v.Oem,
                CombinationKey   = v.CombinationKey,
                AttributeSummary = v.AttributeSummary,
                Media            = new List<EntityMediaLinkEditDto>(v.Media),
            });
        }

        return projected;
    }
}

/// <summary>
/// ÜRÜN → EMTİA SEED'İNİ ÜRETEN ORTAK SINIF (2026-08-20; ana köprü = <c>Product</c> kuralının ileri yönü).
///
/// <para><b>Neden ortak:</b> köprü YEDİ ailede aynı işi yapar — kimliği taşı, kayıt-geneli medyayı bağla,
/// varyantlı ailede nitelik + varyant grafını (varyant medyasıyla) taşı, varyant yoksa ana varyantı ürünün
/// KODUYLA doğur. Bu sınıf yedi kez kopyalansaydı sentinel onarımı gibi ince kurallar altı kopyada
/// unutulurdu; ilki (Mamül) tam bu tuzağa iki kez düştü ve iki ayrı test bunu sabitliyor.</para>
///
/// <para><b>KAYDETMEZ.</b> Yalnız forma seed üretir; kullanıcı emtiaya özel alanları doldurup kendisi
/// kaydeder. Sessizce emtia kaydı açmak, "sınıflandırma manueldir, yazılım tahmin etmez" kuralını delerdi.</para>
/// </summary>
public class ProductCommodityProjectionBuilder : ITransientDependency
{
    /// <summary>Nitelik/varyant grafının sahip bağlamı — medya bağlamıyla aynı dize, ama AYRI kavram
    /// (biri EntityVariant, diğeri EntityMediaLink anahtarı).</summary>
    private const string ProductEntityName = "Product";

    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IEntityVariantGraphService _entityVariant;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly ICurrentCompany _currentCompany;

    public ProductCommodityProjectionBuilder(
        IRepository<Product, Guid> productRepository,
        IEntityVariantGraphService entityVariant,
        IEntityMediaAppService entityMedia,
        ICurrentCompany currentCompany)
    {
        _productRepository = productRepository;
        _entityVariant = entityVariant;
        _entityMedia = entityMedia;
        _currentCompany = currentCompany;
    }

    /// <summary>
    /// KAYDEDİLMEMİŞ ürünün seed'i — <b>DB'ye HİÇ GİTMEZ</b> (2026-08-20).
    ///
    /// <para><b>Neden mümkün:</b> kayıtlı yolun (<see cref="BuildAsync"/>) Product SATIRINDAN okuduğu her şey
    /// beş skalerdir; nitelik/varyant/medya zaten uydu tablolardan (<c>EntityName + EntityId</c>) geliyordu ve
    /// aynı veri kullanıcının AÇIK FORMUNDA duruyor. Yani kayıt şartı referans bütünlüğünden değil, imzanın
    /// <c>Guid</c> almasından doğuyordu; kayıtsız üründe zengin seed bu yüzden sessizce düşürülüyor,
    /// kullanıcı nitelik/varyant/görseli emtia formuna İKİNCİ KEZ giriyordu.</para>
    ///
    /// <para><b>ŞEKİL AYNI KURALDAN OKUNUR</b> (<see cref="CommodityProjectionShapes.ForwardShapeOf"/>) ve
    /// varyant kolu kayıtlı yolla AYNI kodu kullanır: iki yolun aynı veriden aynı seed'i üretmesini şu test
    /// sabitler — <c>ProductCommodityProjectionTests.An_unsaved_product_seeds_the_commodity_form_exactly_like_a_saved_one</c>.
    /// Taslak için ayrı bir kod yazmak, sentinel onarımı gibi ince kuralların kopyalardan birinde unutulması
    /// demekti — bu projede tam olarak yaşanmış bir desen.</para>
    ///
    /// <para><b>REÇETE TAŞINMAZ:</b> reçete satırı varyantın DB kimliğine yazılır, o kimlik ürün kaydedilince
    /// doğar. Seed kayıtsız çalışır, reçete yayılımı çalışmaz — köprünün gerçek yapısal sınırı budur.</para>
    /// </summary>
    public virtual ProductProjectionSeed BuildFromDraft(ProductDraftSeedDto draft, ProductProjectionShape shape)
    {
        Check.NotNull(draft, nameof(draft));

        var seed = new ProductProjectionSeed
        {
            // KİMLİK YOK: taslağın ürün kaydı henüz doğmadı. Uydurma bir Guid yazmak, seed'i tüketen tarafa
            // "bu ürün kayıtlı" derdi ve o kimlikle yapılan her okuma sessizce boş dönerdi.
            ProductId   = Guid.Empty,
            Code        = draft.Code,
            Name        = draft.Name,
            Description = draft.Description,

            // SAHİPLİK İSTEMCİDEN ALINMAZ (CLAUDE.md §6): çalışılan şirketten CompanyOwnershipGuard ile çözülür, şirket yoksa fail-closed.
            CompanyId   = CompanyOwnershipGuard.ResolveOwnerCompanyId(_currentCompany),
            VatRate     = draft.VatRate,
        };

        if (shape == ProductProjectionShape.Identity)
        {
            return seed;
        }

        // Medya bağı MediaId ile kopyalanır: görsel panele eklendiği anda kütüphaneye yüklendiğinden kimlik
        // ürün kaydedilmeden ÖNCE de gerçektir (medya içeriği değişmezdir — dosya yeniden yüklenmez).
        seed.Media.AddRange(CloneLinks(draft.Media));

        if (shape != ProductProjectionShape.FullGraph)
        {
            return seed;
        }

        seed.Attributes.AddRange(ProjectAttributes(draft.Attributes));

        foreach (var variant in draft.Variants.Where(v => !v.IsDeleted))
        {
            seed.Variants.Add(ProjectVariant(variant, CloneLinks(variant.Media)));
        }

        EnsureSeedVariants(seed, draft.Code, draft.Name);
        return seed;
    }

    /// <summary>Ürünün emtia seed'ini üretir (PERSİSTSİZ) — <paramref name="shape"/> ailenin kategorisidir.
    /// Kaydı OLMAYAN ürün için <see cref="BuildFromDraft"/>.</summary>
    public virtual async Task<ProductProjectionSeed> BuildAsync(Guid productId, ProductProjectionShape shape)
    {
        // TEK FAIL-FAST KONTROLÜ: aşağıdaki okumaların HİÇBİRİ bilinmeyen kimlikte hata vermez — medya boş liste
        // döner, varyant grafı sentinel bir ana varyant UYDURUR. Bu satır kalkarsa metot patlamaz, UYDURMA bir
        // seed döner ve hata sessizleşir. Test şunu korur:
        // ProductCommodityProjectionTests.The_saved_path_fails_fast_on_an_unknown_product_instead_of_seeding_an_invented_record.
        var product = await _productRepository.FindAsync(productId)
            ?? throw new BusinessException("TradeXpress:Product:NotFound");

        var seed = new ProductProjectionSeed
        {
            ProductId   = product.Id,
            Code        = product.Code,
            Name        = product.Name,
            Description = product.Description,
            CompanyId   = product.CompanyId,
            VatRate     = product.VatRate,
        };

        if (shape == ProductProjectionShape.Identity)
        {
            return seed;
        }

        // MEDYA BAĞLAM-BAĞLAM KOPYALANIR (2026-08-06 Hakan kararı): iki depo AYRI kalır ve biri diğerinden
        // TÜRETİLMEZ. Kayıt geneli → kayıt geneli, varyant → varyant.
        seed.Media.AddRange(await _entityMedia.GetForAsync(MediaEntityNames.Product, product.Id));

        if (shape != ProductProjectionShape.FullGraph)
        {
            return seed;
        }

        await LoadGraphIntoAsync(seed, product);
        return seed;
    }

    private async Task LoadGraphIntoAsync(ProductProjectionSeed seed, Product product)
    {
        // Nitelik + varyant grafı ORTAK sistemden okunur ("Product" bağlamı) — hedef aile aynı jenerik
        // tipleri kullandığı için dönüşüm alan-eşlemesinden ibarettir.
        var graph = await _entityVariant.LoadGraphAsync(ProductEntityName, product.Id);

        // Nitelikler KİMLİKSİZ taşınır (Id yazılmaz): hedef emtianın kendi nitelik düğümleri doğar, kaynak
        // ürünün düğümleri sahiplenilmez.
        //
        // AMA ClientKey KOPYALANIR — o bir DB kimliği DEĞİL, kombinasyonun istemci-taraflı imzasıdır:
        // varyantın CombinationKey'i ("|" ile birleştirilmiş değer ClientKey'leri) tam olarak bu anahtarları
        // gösterir. Yeni ClientKey üretilirse imza kaynağını kaybeder ve kayıtta hiçbir varyant çözülemez
        // (aşağıdaki "kombinasyon kimliği" notu).
        seed.Attributes.AddRange(ProjectAttributes(graph.Attributes));

        // VARYANTLAR TAŞINIR, YENİDEN ÜRETİLMEZ: kartezyeni yeniden kurmak kullanıcının ürün üzerinde
        // yaptığı elemeleri (silinmiş kombinasyonlar) geri getirirdi.
        //
        // GÖRSELLER VARYANTTA YAŞAR: medya kayıt genelinde değil "{Entity}Variant" bağlamında, varyant
        // id'siyle tutulur. Bağlar MediaId ile KOPYALANIR, dosya YENİDEN YÜKLENMEZ.
        //
        // KOMBİNASYON KİMLİĞİ (CombinationKey) DE TAŞINIR — düşürülemez: hedef emtia kaydedilirken
        // senkronizatör kartezyeni DB'de yeniden materyalize eder ve taşınan satırların özelleştirmeleri
        // (barkod/GTIN/MPN/stok/açıklama + VARYANT MEDYASI) o DB varyantlarına ancak bu imzayla oturur
        // (EntityVariantGraphService.ResolveTargetVariant). İmza boşsa ana varyant DIŞINDAKİ her satır
        // sessizce ATLANIR — istisna yok, log yok; kullanıcı formda doğru gördüğü barkodun ve görselin
        // kayıttan sonra yok olduğunu fark eder. Bu yüzden "salt görüntü" gibi duran bu alan aslında
        // KİMLİKtir ve değer ClientKey'leriyle birlikte bir bütündür.
        foreach (var v in graph.Variants.Where(x => !x.IsDeleted))
        {
            // Kaynak varyantın DB kimliği yoksa (kaydedilmemiş graf düğümü) bağlanacak medya da yoktur.
            var media = v.Id != Guid.Empty
                ? await _entityMedia.GetForAsync(MediaEntityNames.ProductVariant, v.Id)
                : new List<EntityMediaLinkEditDto>();

            seed.Variants.Add(ProjectVariant(v, media));
        }

        EnsureSeedVariants(seed, product.Code, product.Name);
    }

    /// <summary>Nitelik grafını KİMLİKSİZ taşır (Id yazılmaz): hedef emtianın kendi nitelik düğümleri doğar,
    /// kaynak ürünün düğümleri sahiplenilmez.
    ///
    /// <para><b>AMA ClientKey KOPYALANIR</b> — o bir DB kimliği DEĞİL, kombinasyonun istemci-taraflı imzasıdır:
    /// varyantın <c>CombinationKey</c>'i ("|" ile birleştirilmiş değer ClientKey'leri) tam olarak bu anahtarları
    /// gösterir. Yeni ClientKey üretilirse imza kaynağını kaybeder ve kayıtta ana varyant dışındaki hiçbir
    /// varyant çözülemez (aşağıdaki "kombinasyon kimliği" notu).</para></summary>
    private static List<EntityAttributeGraphDto> ProjectAttributes(IEnumerable<EntityAttributeGraphDto> source)
    {
        return source
            .Where(a => !a.IsDeleted)
            .Select(a => new EntityAttributeGraphDto
            {
                ClientKey    = a.ClientKey,
                Name         = a.Name,
                DisplayOrder = a.DisplayOrder,
                Values = a.Values
                    .Where(v => !v.IsDeleted)
                    .Select(v => new EntityAttributeValueGraphDto
                    {
                        ClientKey    = v.ClientKey,
                        Value        = v.Value,
                        DisplayOrder = v.DisplayOrder,
                    })
                    .ToList(),
            })
            .ToList();
    }

    /// <summary>Tek varyantın seed karşılığı — medyası ÇAĞIRANDAN gelir (kayıtlı yolda DB'den okunur, taslak
    /// yolda formun elindeki liste doğrudan verilir). İki yolun da AYNI alan kümesini taşıması bu metodun
    /// varlık sebebidir: kopyalansaydı biri Oem'i, diğeri imzayı düşürürdü — ikisi de bu projede yaşandı.</summary>
    private static EntityVariantGraphDto ProjectVariant(EntityVariantGraphDto v, List<EntityMediaLinkEditDto> media)
    {
        return new EntityVariantGraphDto
        {
            IsMain           = v.IsMain,
            Code             = v.Code,
            Name             = v.Name,
            Description      = v.Description,
            IsActive         = v.IsActive,
            Barcode          = v.Barcode,
            Gtin             = v.Gtin,
            Mpn              = v.Mpn,

            // TİCARİ KİMLİKLERİN ÜÇÜ DE TAŞINIR (2026-08-20): Oem eskiden DÜŞÜYORDU ve eksiklik sessizdi —
            // kayıt yolu SetTradeIdentifiers(Gtin, Mpn, Oem) çağırdığı için üçüncü alan her seferinde null
            // yazılıyordu. Ters yön (emtia → ürün) Oem'i zaten taşıyor; asimetri köprüyü tek yönde kayıp
            // veren bir yol hâline getirmişti.
            Oem              = v.Oem,
            CombinationKey   = v.CombinationKey,
            AttributeSummary = v.AttributeSummary,
            Media            = new List<EntityMediaLinkEditDto>(media),
        };
    }

    /// <summary>
    /// Medya bağlarını KOPYALAR — taslak yolunda zorunlu.
    ///
    /// <para><b>Neden:</b> Blazor Server'da app service çağrısı in-process yapılır, serileştirme sınırı YOKTUR;
    /// istemcinin verdiği nesne referansları sunucuya AYNEN geçer. Bağ nesneleri paylaşılsaydı emtia formunda
    /// yapılan bir düzenleme (sıra değiştirme, cover <c>IsDefault</c> işaretleme, satır pasifleştirme) AÇIK ÜRÜN FORMUNU da
    /// değiştirirdi — kullanıcı hiç dokunmadığı bir formun bozulduğunu ancak kaydederken fark ederdi. Kayıtlı
    /// yolda böyle bir risk yok: bağlar DB'den taze okunur.</para>
    ///
    /// <para><c>ClientKey</c> yeniden üretilir (satır anahtarı kalıcı değildir); <c>Media</c> görüntü nesnesi
    /// TAŞINIR çünkü salt-okunur bir çözümlemedir ve panelin önizlemeyi ikinci kez çözmesini gereksiz kılar.</para>
    /// </summary>
    private static List<EntityMediaLinkEditDto> CloneLinks(IEnumerable<EntityMediaLinkEditDto> links)
    {
        return links
            .Select(l => new EntityMediaLinkEditDto
            {
                MediaId      = l.MediaId,
                DisplayOrder = l.DisplayOrder,
                IsDefault    = l.IsDefault,
                IsActive     = l.IsActive,
                Media        = l.Media,
            })
            .ToList();
    }

    /// <summary>Varyant listesinin son hâli: hiç varyant yoksa ana varyantı SAHİBİN koduyla doğur, sentinel
    /// kod geldiyse onar. İki yol da (kayıtlı ürün · taslak) buradan geçer.</summary>
    private static void EnsureSeedVariants(ProductProjectionSeed seed, string ownerCode, string ownerName)
    {
        // Hiç varyant yoksa form boş kalmasın — ana varyant KAYIT KODUYLA doğar ("ANAVARYANT" sentinel'i
        // yerine): tek varyant ayrım değildir, ayırt edici bir kod taşımasının anlamı yok.
        if (seed.Variants.Count == 0)
        {
            seed.Variants.Add(new EntityVariantGraphDto
            {
                IsMain   = true,
                Code     = ownerCode,
                Name     = ownerName,
                IsActive = true,
            });
        }

        RewriteSentinelMainVariant(seed.Variants, ownerCode, ownerName);
    }

    /// <summary>
    /// SENTINEL ONARIMI — ana varyantın kodu <c>ANAVARYANT</c> ise SAHİBİN koduna çevrilir.
    ///
    /// <para><b>Neden yukarıdaki "boş liste" dalı YETMİYOR</b> (2026-08-10, testle yakalandı): varyantsız
    /// kayıtta <c>LoadGraphAsync</c> boş liste DÖNDÜRMEZ — ana varyantı sentinel yer tutucusuyla üretip
    /// döndürür. Dolayısıyla liste hiçbir zaman boş gelmiyor ve o dal hiç çalışmıyordu; projeksiyon
    /// sentinel'i olduğu gibi emtiaya taşıyordu. O kod pazaryerine SKU olarak gidebildiğinden sessiz değil
    /// PAHALI bir hatadır.</para>
    ///
    /// <para><b>Neden kaynağında (LoadGraphAsync) değil BURADA:</b> yer tutucu, sahibin kimliğinin
    /// bilinmediği genel bir yükleme yolunda meşrudur (form kimlik alanını zaten gizler). Sahibin kodu
    /// yalnız BURADA biliniyor. Kaynağı değiştirmek onu tüketen tüm formları etkilerdi.</para>
    /// </summary>
    private static void RewriteSentinelMainVariant(List<EntityVariantGraphDto> variants, string ownerCode, string ownerName)
    {
        foreach (var variant in variants)
        {
            if (string.Equals(variant.Code, EntityVariantConsts.MainVariantCode, StringComparison.Ordinal))
            {
                variant.Code = ownerCode;
                variant.Name = ownerName;
            }
        }
    }
}
