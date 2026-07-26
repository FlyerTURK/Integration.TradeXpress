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
    public const int ImageUrlMaxLength = 1000;   // görsel URL (https, SSL)
    public const int MaxImageCount = 8;          // N11 ürün başına en fazla 8 görsel
    public const int ImageBlobNameMaxLength = 256;   // blob adı = path anahtarı ("Products/KOD[/VARYANTKOD]/GORSEL0001.ext"); Guid'den uzun olabilir
    public const int ImageFileNameMaxLength = 256;   // yüklenen dosyanın orijinal adı (görüntü)
    public const int MaxImageSizeBytes = 10 * 1024 * 1024;  // yükleme sınırı 10 MB (Etsy tavanı 20MB; Blazor circuit 16MB mesaj sınırı → güvenli 10MB). Metal bu sabiti alias'lar.

    // ── Ürün özelleştirme (personalization; pazaryeri-genel, Etsy who_made deseni). Talimat + karakter sınırı. ──
    /// <summary>Kişiselleştirme talimatı — EtsyProduct PersonalizationInstructionsMaxLength (256) ile HİZALI.</summary>
    public const int PersonalizationInstructionsMaxLength = 256;

    /// <summary>Kişiselleştirme karakter sınırının (müşteri girişi) üst tavanı — Etsy personalization_char_count_max = 256.
    /// Değer verilirse 1..bu aralıkta olmalı (entity fail-fast).</summary>
    public const int PersonalizationCharCountMaxLimit = 256;

    /// <summary>Varyant ticari kimlik kodları (barcode/GTIN/MPN/OEM) — marketplace SKU eşleşmesi + katalog.</summary>
    public const int TradeIdentifierMaxLength = 64;

    // ── Pazaryeri-genel varsayılan alanlar (ürün-seviyesi; kanal-ürünü devralır + override eder) ──
    /// <summary>Varsayılan kargo şablonu adı (N11ProductConsts.ShipmentTemplateNameMaxLength paritesi).</summary>
    public const int ShipmentTemplateNameMaxLength = 128;

    /// <summary>Satıcı notu (kısa düz metin; N11ProductConsts.SellerNoteMaxLength paritesi).</summary>
    public const int SellerNoteMaxLength = 500;

    // Ürün özelleştirme alanı (owned → JSON; N11 SpecialInfo sınırlarıyla hizalı — key=müşteri giriş etiketi zorunlu).
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
