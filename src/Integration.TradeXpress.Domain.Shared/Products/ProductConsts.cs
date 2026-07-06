namespace Integration.TradeXpress.Products;

/// <summary>Product / ProductVariant alan sınırları. Katalog kodlarından (32) daha uzun; SKU marketplace
/// satıcı-kodları + uzun başlık/açıklama içindir (N11 title/description). Adım 2+'de gerekirse ayarlanır.</summary>
public static class ProductConsts
{
    public const int CodeMaxLength = 64;         // satıcı SKU (sellerStockCode marketplace'te uzun olabilir)
    public const int NameMaxLength = 256;        // marketplace başlığı
    public const int DescriptionMaxLength = 4000;// marketplace açıklaması (uzun/HTML)

    // Base (0-attribute) ana varyantın SABİT kimliği — ürün kodundan TÜRETİLMEZ (2026-07-05 ürün kararı).
    // OrgTree HQ Branch / Default Vault deseniyle hizalı (const-tabanlı ad; BranchConsts.DefaultHeadquarters* paritesi).
    public const string MainVariantCode = "ANAVARYANT";
    public const string MainVariantName = "Ana Varyant";
}
