namespace Integration.TradeXpress.Products;

/// <summary>Product attribute/value alan sınırları + iş kuralı sabitleri.</summary>
public static class ProductAttributeConsts
{
    public const int NameMaxLength = 64;   // ör. "Renk", "Beden"
    public const int ValueMaxLength = 128; // ör. "Kırmızı", "M"

    /// <summary>Ürün başına en fazla attribute sayısı (ürün kuralı 2026-07-05).</summary>
    public const int MaxAttributesPerProduct = 5;
}
