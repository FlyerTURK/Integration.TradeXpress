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
    public const int ImageBlobNameMaxLength = 64;    // blob adı (Guid + uzantı)
    public const int ImageFileNameMaxLength = 256;   // yüklenen dosyanın orijinal adı (görüntü)
    public const int MaxImageSizeBytes = 4 * 1024 * 1024;   // yükleme sınırı 4 MB (upload + önizleme yükü)

    /// <summary>Varyant ticari kimlik kodları (barcode/GTIN/MPN/OEM) — marketplace SKU eşleşmesi + katalog.</summary>
    public const int TradeIdentifierMaxLength = 64;

    // Base (0-attribute) ana varyantın SABİT kimliği — ürün kodundan TÜRETİLMEZ (2026-07-05 ürün kararı).
    // OrgTree HQ Branch / Default Vault deseniyle hizalı (const-tabanlı ad; BranchConsts.DefaultHeadquarters* paritesi).
    public const string MainVariantCode = "ANAVARYANT";
    public const string MainVariantName = "Ana Varyant";
}
