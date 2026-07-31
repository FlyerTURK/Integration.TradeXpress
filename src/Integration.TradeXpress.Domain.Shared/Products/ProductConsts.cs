namespace Integration.TradeXpress.Products;

/// <summary>Product / ProductVariant alan sınırları. Katalog kodlarından (32) daha uzun; SKU marketplace
/// satıcı-kodları + uzun başlık/açıklama içindir (N11 title/description). Adım 2+'de gerekirse ayarlanır.</summary>
public static class ProductConsts
{
    public const int CodeMaxLength = 64;         // satıcı SKU (sellerStockCode marketplace'te uzun olabilir)
    public const int NameMaxLength = 256;        // marketplace başlığı
    public const int DescriptionMaxLength = 4000;// marketplace açıklaması (uzun/HTML)

    // ── Satılabilir veri (Adım 2: fiyat/stok/görsel — marketplace zorunlu alanları) ──
    public const int SalePricePrecision = 18;    // satış/liste fiyatı (marketplace price/optionPrice)
    public const int SalePriceScale = 2;
    public const int MaxImageCount = 8;          // N11 ürün başına en fazla 8 görsel (push + import kırpma sınırı; içerik DAM'da)

    // Kişiselleştirme talimatı/karakter sınırı sabitleri 2026-07-28'de KALDIRILDI: Etsy'nin tek-kutulu modeli
    // 2026-04-09'da kapandı, kişiselleştirme artık SpecialInfo satırlarıyla (soru başına ayar) ifade ediliyor.

    /// <summary>Varyant ticari kimlik kodları (barcode/GTIN/MPN/OEM) — marketplace SKU eşleşmesi + katalog.</summary>
    public const int TradeIdentifierMaxLength = 64;

    // ── Pazaryeri-genel varsayılan alanlar (ürün-seviyesi; kanal-ürünü devralır + override eder) ──

    /// <summary>Satıcı notu (kısa düz metin; N11ProductConsts.SellerNoteMaxLength paritesi).</summary>
    public const int SellerNoteMaxLength = 500;

    // Ürün özelleştirme alanı (owned → JSON; N11 SpecialInfo sınırlarıyla hizalı — key=müşteri giriş etiketi zorunlu).
    /// <summary>Yeni üründe varsayılan hazırlık süresi (gün) — 2026-07-28 Hakan: 1 gün pratikte yetişmiyor,
    /// gerçekçi varsayılan 3. En az 1'dir (0 gün "aynı saniye kargoda" demek; pazaryerleri kabul etmez).</summary>
    public const int DefaultPreparingDay = 3;

    /// <summary>Ürün genel özelliği ("Ayar: 22K") değeri — pazaryeri nitelik değerleri kısa metinlerdir
    /// (N11 productAttribute value sınırıyla hizalı).</summary>
    public const int SpecificationValueMaxLength = 512;

    public const int SpecialInfoKeyMaxLength = 64;
    public const int SpecialInfoValueMaxLength = 20000;   // HTML/uzun örnek olabilir

    // Ürüne atanan eklenti (owned → JSON) — satır notu.
    public const int AddOnNoteMaxLength = 512;

    // Base (0-attribute) ana varyantın SABİT kimliği — ürün kodundan TÜRETİLMEZ (2026-07-05 ürün kararı).
    // OrgTree HQ Branch / Default Vault deseniyle hizalı (const-tabanlı ad; BranchConsts.DefaultHeadquarters* paritesi).
    public const string MainVariantCode = "ANAVARYANT";
    public const string MainVariantName = "Ana Varyant";

    /// <summary>Muadil-Çoklu modda materyalize edilecek en fazla varyant sayısı (Rank sırasıyla). Motorun
    /// TopN'i (50) müşteri-yüzü için fazla — kanala 50 aynı-gramaj SKU anlamsız; tolerans/override ile
    /// daraltılır, bu tavan son emniyettir. (ADR-PRODUCT-ORCHESTRATION; 2026-07-25.)</summary>
    public const int SubstitutionMaterializedVariantMax = 10;
}
