using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Bir sipariş satırının pazaryerinden gelen SNAPSHOT'ı — <see cref="OrderLine"/> kuruluş girdisi (VoucherLineInput
/// deseni). TÜM alanlar kendi başına anlamlıdır: satır hiçbir yerel <see cref="ProductVariant"/> join'i OLMADAN
/// eksiksiz görüntülenir/yönetilir (ürün-agnostik; yerel ürün silinse/hiç olmasa da satır tam kullanılabilir).
/// <see cref="ProductVariantId"/> yalnız OPSİYONEL zenginleştirme (id-only, sert FK yok) — yokluğu NORMAL durumdur,
/// hata değil; sonraki dilim (O1) doldurmaya çalışır ama kalıcı null kabul edilebilir son durumdur.
/// </summary>
public sealed record OrderLineSnapshot(
    string? RemoteLineId,
    string? Barcode,
    string? StockCode,
    string ProductNameSnapshot,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string? RemoteLineStatus,
    Guid? ProductVariantId);
