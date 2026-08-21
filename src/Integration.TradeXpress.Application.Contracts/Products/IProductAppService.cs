using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Products;

public interface IProductAppService : ICrudAppService<
    ProductGetDto,
    ProductListDto,
    Guid,
    ProductListRequestDto,
    ProductCreateDto,
    ProductUpdateDto>
{
    /// <summary>Nitelik grafından varyantları ÜRETİR — PERSISTSİZ önizleme (kayıt gerekmez, DB'ye yazmaz).
    /// Kartezyen + kod/ad türetme <c>ProductVariantSynchronizer</c> ile AYNI mantık; ilk satır IsMain (display),
    /// hepsi aktif, <c>CombinationKey</c> dolu. Değersiz nitelik → <c>TradeXpress:ProductAttribute:ValueRequired</c>.</summary>
    Task<List<ProductVariantGraphDto>> GenerateVariantsAsync(ProductVariantGenerateRequestDto input);

    /// <summary>Bir varyantın reçete satırlarının CANLI maliyetini PERSISTSİZ hesaplar (TAM KAYIT gerekmez) —
    /// <c>GetAsync</c> projeksiyonuyla AYNI motor (ülke birimine rebase + calculator). Satır-başı Uygulanacak Bedel /
    /// Satır Maliyeti / Ara Toplam + varyant net'i döner. DB'ye YAZMAZ (design-time; ledger'a değmez).</summary>
    Task<ProductRecipeCostResultDto> CalculateRecipeCostAsync(ProductRecipeCostRequestDto input);

    /// <summary>
    /// Ürünün genel özelliklerini bir KANALIN nitelik alanlarına çevirir (kategori eşleştirmeleri üzerinden).
    /// Kanal ürünü kurulurken nitelikler bununla ön-doldurulur — kullanıcı aynı bilgiyi ikinci kez girmesin.
    /// </summary>
    Task<List<ProductChannelAttributeDto>> ResolveChannelAttributesAsync(ProductChannelAttributeResolveDto input);

    /// <summary>Çalışılan şirketteki SINIFLANDIRILMAMIŞ ürünler — reçetesi hiç olmayanlar. Sihirbazın
    /// sınıflandırma adımı bu listeyi doldurur.
    /// <para><b>Kanal parametresi YOK</b> (bilinçli): liste kanaldan değil ŞİRKETTEN çıkar, böylece adım
    /// eski içe aktarımların bıraktığı ürünleri de yakalar — yalnız "bu turda gelenler"e bakmak, geçmişte
    /// atlanmış ürünleri sonsuza dek görünmez kılardı.</para></summary>
    Task<List<ProductCommodityCandidateDto>> GetUnclassifiedProductsAsync();

    /// <summary>Sihirbaz sınıflandırmasını uygular: emtia kaydı (gerekiyorsa) → reçete satırı → stok
    /// politikası → otorite devri → stok yeniden-hesap job'ı. TEK çağrı (ürün başına round-trip YOK).</summary>
    Task<ProductCommodityProvisionResultDto> ProvisionCommoditiesAsync(ProductCommodityProvisionInputDto input);

    /// <summary>İNSAN yolu: varyantları satışa DOĞRULAR (<c>Draft/Closed/Suspended → Ready</c>) ve ürünü
    /// satılabilir işaretler.
    ///
    /// <para><b>Kapatılan açık:</b> push guard'ı (<c>VariantSaleReadinessResolver</c>) fail-closed ÇALIŞIYORDU ama onayı verecek insan yolu hiç
    /// yoktu — <c>MarkVerified</c>/<c>MarkSaleReady</c>'nin src'de sıfır çağıranı vardı. Sonuç: canlıda
    /// 165/165 varyant <c>Draft</c> ve HİÇBİR ürün pazaryerine çıkamıyordu. Hata sessiz: guard doğru çalışıyor,
    /// yalnız kimse guard'ı açamıyor.</para>
    ///
    /// <para>Onay anındaki reçete stamp'i (<c>VerifiedRecipeStamp</c>) saklanır; reçete sonradan değişirse onay KENDİLİĞİNDEN düşer
    /// (ayrı bir olay altyapısı bilinçli olarak yok). Guard fail-closed KALIR — burada eklenen yalnız insan
    /// yoludur.</para></summary>
    Task<ProductSaleVerifyResultDto> VerifySaleReadinessAsync(ProductSaleVerifyInputDto input);

    /// <summary>ÜRÜNÜN SATIŞA HAZIRLIK PANELİ (2026-08-19): "bu ürün neden satışta değil, sıradaki adım ne, nereye tıklayacağım?"
    /// sorusunun tek DTO'da cevabı — sıralı kontrol listesi, issue satırları (<c>SaleReadinessIssueDto</c>;
    /// Error/Warning/Info), varyant sayaçları (<c>ProductSaleValidator</c>'ın yargısından okunur), kanal ürünü
    /// satırları ve aksiyon uygunlukları. Otomatik validasyonun
    /// (<c>ProductSaleValidator</c>) insanın gözüne çıktığı yer; aynı validator doğrulamada da koşar.</summary>
    Task<ProductSaleReadinessDto> GetSaleReadinessAsync(Guid productId);

    /// <summary>Ürünün MAMÜL projeksiyonunu üretir (PERSİSTSİZ) — sınıflandırma adımında "Emtia Formunu Aç"
    /// formunu ön-doldurmak için. Kod/ad/KDV + nitelik + varyant grafı + iki bağlam medya taşınır; kullanıcı
    /// Good'a özel alanları doldurup kendisi kaydeder.</summary>
    Task<Goods.GoodGetDto> ProjectToGoodAsync(Guid productId);

    // ── ÜRÜN → EMTİA KÖPRÜSÜNÜN KALAN ALTI ProjectTo*Async UCU (2026-08-20) ──────────────────────────
    // CLAUDE.md §6 "ANA KÖPRÜ = Product": çevrim YEDİ ailenin hepsi için olmalı. AİLE BAŞINA TİPLİ ENDPOINT —
    // tek tipsiz endpoint (Task<object> + ProcessType) sözleşmeyi kaybettirirdi: çağıran hangi DTO'yu aldığını
    // derleyiciden değil belgeden öğrenirdi ve yanlış cast ancak çalışma anında patlardı.
    // Taşınan/taşınmayan alanların gerekçesi ProductToCommodityProjector'da.

    /// <summary>Ürünün MADEN projeksiyonu (PERSİSTSİZ) — tam varyantlı aile: nitelik + varyant grafı + iki bağlam
    /// medya taşınır. Milyem/takip birimi gibi TEKNİK alanlar taşınmaz (kullanıcı maden formunda verir).</summary>
    Task<Metals.MetalGetDto> ProjectToMetalAsync(Guid productId);

    /// <summary>Ürünün MÜCEVHER projeksiyonu (PERSİSTSİZ) — "uzantısız varyantlı" aile (CLAUDE.md §6 ②: varyant
    /// paneli var, <c>*VariantDetail</c> uzantısı yok): varyant grafı taşınır, fiyat (entity seviyesinde yaşar)
    /// taşınmaz.</summary>
    Task<Jewelries.JewelryGetDto> ProjectToJewelryAsync(Guid productId);

    /// <summary>Ürünün TAŞ projeksiyonu (PERSİSTSİZ) — aile VARYANTSIZDIR: yalnız kimlik + kayıt-geneli medya
    /// taşınır, varyant/nitelik grafı BİLİNÇLE boş kalır.</summary>
    Task<Stones.StoneGetDto> ProjectToStoneAsync(Guid productId);

    /// <summary>Ürünün HURDA projeksiyonu (PERSİSTSİZ) — yalnız kimlik (DTO'da medya/varyant alanı yok).</summary>
    Task<Scraps.ScrapGetDto> ProjectToScrapAsync(Guid productId);

    /// <summary>Ürünün VADELİ projeksiyonu (PERSİSTSİZ) — yalnız kimlik ("vadeli varyant barındırmaz").</summary>
    Task<Futures.FutureGetDto> ProjectToFutureAsync(Guid productId);

    /// <summary>Ürünün HİZMET projeksiyonu (PERSİSTSİZ) — yalnız kimlik (stoklanmayanın varyantı/görseli olmaz).</summary>
    Task<Services.ServiceGetDto> ProjectToServiceAsync(Guid productId);

    // ── KAYDEDİLMEMİŞ ÜRÜNÜN SEED'İ — AYNI YEDİ ProjectDraftTo*Async, DB'SİZ (2026-08-20) ────────────
    //
    // NEDEN AYRI ENDPOINT, "Guid ya da taslak" taşıyan tek DTO DEĞİL: "ikisinden TAM BİRİ dolu" kuralını
    // derleyici denetleyemez; ikisi de boş ya da ikisi de dolu gelen bir çağrı ancak belgeyle yasaklanabilirdi.
    // Kayıtlı ürün yolu (yukarıdaki Guid alan endpoint'ler) olduğu gibi DURUYOR.
    //
    // NEDEN GEREKLİ: köprünün taşıdığı her şey graftır (nitelik · varyant · iki bağlam medya) ve aynı veri
    // kullanıcının açık formunda zaten duruyor; kaydı şart koşan tek nokta imzaydı. Kayıtsız üründe zengin
    // seed bu yüzden sessizce düşürülüyor ve kullanıcı aynı bilgiyi emtia formuna İKİNCİ KEZ giriyordu.
    //
    // SINIR: reçete yayılımı BURADA YOKTUR — reçete satırı varyantın DB kimliğine yazılır, o kimlik ürün
    // kaydedilince doğar. Seed kayıtsız çalışır, reçete kayıtla gelir.

    /// <summary>Kaydedilmemiş üründen MAMÜL projeksiyonu (PERSİSTSİZ, DB'ye gitmez).</summary>
    Task<Goods.GoodGetDto> ProjectDraftToGoodAsync(ProductDraftSeedDto input);

    /// <summary>Kaydedilmemiş üründen MADEN projeksiyonu — nitelik + varyant grafı ve iki bağlam medya taşınır.</summary>
    Task<Metals.MetalGetDto> ProjectDraftToMetalAsync(ProductDraftSeedDto input);

    /// <summary>Kaydedilmemiş üründen MÜCEVHER projeksiyonu — varyant grafı taşınır, fiyat taşınmaz.</summary>
    Task<Jewelries.JewelryGetDto> ProjectDraftToJewelryAsync(ProductDraftSeedDto input);

    /// <summary>Kaydedilmemiş üründen TAŞ projeksiyonu — kimlik + kayıt-geneli medya (aile VARYANTSIZDIR).</summary>
    Task<Stones.StoneGetDto> ProjectDraftToStoneAsync(ProductDraftSeedDto input);

    /// <summary>Kaydedilmemiş üründen HURDA projeksiyonu — yalnız kimlik.</summary>
    Task<Scraps.ScrapGetDto> ProjectDraftToScrapAsync(ProductDraftSeedDto input);

    /// <summary>Kaydedilmemiş üründen VADELİ projeksiyonu — yalnız kimlik.</summary>
    Task<Futures.FutureGetDto> ProjectDraftToFutureAsync(ProductDraftSeedDto input);

    /// <summary>Kaydedilmemiş üründen HİZMET projeksiyonu — yalnız kimlik.</summary>
    Task<Services.ServiceGetDto> ProjectDraftToServiceAsync(ProductDraftSeedDto input);
}
