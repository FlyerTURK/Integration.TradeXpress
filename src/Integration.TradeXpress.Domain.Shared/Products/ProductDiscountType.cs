namespace Integration.TradeXpress.Products;

/// <summary>Ürün indirim tipi — marketplace listeleme indirimi (N11 SellerProductDiscount / ProductDiscountRequest).
/// Ürün-seviyesi (tüm varyantlar + tüm kanallar aynı indirim; 2026-07-07 kullanıcı kararı).</summary>
public enum ProductDiscountType
{
    /// <summary>İndirim yok.</summary>
    None = 0,

    /// <summary>Sabit tutar indirimi (liste fiyatından düşülür).</summary>
    Amount = 1,

    /// <summary>Yüzde indirimi (0–100).</summary>
    Percentage = 2,
}
