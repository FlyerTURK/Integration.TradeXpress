using System;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Trendyol ham sevkiyat paketi durumu → NÖTR <see cref="OrderStatus"/> eşlemesi (SAF STATİK; birim testli). Ham
/// durum ayrıca <c>Order.RemoteStatus</c>'te saklanır — bu yalnız kanallar-arası ortak filtre/görüntü yorumudur.
/// Bilinmeyen/boş durum sessizce "New" varsayılMAZ → <see cref="OrderStatus.Unknown"/> (belirsizlik taşınır).
/// Trendyol paket durumları: Created / Picking / Invoiced / Shipped / AtCollectionPoint / Cancelled / UnPacked /
/// Delivered / UnDelivered / Returned / Repack / UnSupplied (kamuya açık dokümana göre; case-insensitive eşlenir).
/// </summary>
public static class TrendyolOrderStatusMapper
{
    public static OrderStatus Map(string? remoteStatus)
    {
        if (string.IsNullOrWhiteSpace(remoteStatus))
        {
            return OrderStatus.Unknown;
        }

        return remoteStatus.Trim().ToLowerInvariant() switch
        {
            "created" or "awaiting" => OrderStatus.New,
            "picking" or "invoiced" or "repack" => OrderStatus.Processing,
            "shipped" or "atcollectionpoint" => OrderStatus.Shipped,
            "delivered" => OrderStatus.Delivered,
            "cancelled" or "canceled" or "unpacked" or "unsupplied" => OrderStatus.Cancelled,
            "returned" or "undelivered" => OrderStatus.Returned,
            _ => OrderStatus.Unknown,
        };
    }
}
