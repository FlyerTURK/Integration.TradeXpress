using System;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Etsy receipt durumu → NÖTR <see cref="OrderStatus"/> eşlemesi (SAF STATİK; birim testli). Ham durum ayrıca
/// <c>Order.RemoteStatus</c>'te saklanır (kanallar-arası ortak filtre/görüntü yorumu). Bilinmeyen/boş durum sessizce
/// "New" varsayılMAZ → <see cref="OrderStatus.Unknown"/>. Etsy Open API v3 receipt <c>status</c> değerleri (kamuya açık
/// dokümana göre; case-insensitive): open / unpaid / payment processing / paid / processing / completed / canceled /
/// fully refunded / partially refunded. (Kargolama <c>is_shipped</c> ayrı bayrak; burada yalnız status yorumlanır.)
/// </summary>
public static class EtsyOrderStatusMapper
{
    public static OrderStatus Map(string? remoteStatus)
    {
        if (string.IsNullOrWhiteSpace(remoteStatus))
        {
            return OrderStatus.Unknown;
        }

        // Etsy bazı durumları alt-çizgi ya da boşlukla döndürür (payment_processing / "Payment Processing") → normalize.
        return remoteStatus.Trim().ToLowerInvariant().Replace('_', ' ') switch
        {
            "open" or "unpaid" or "payment processing" => OrderStatus.New,
            "paid" or "processing" => OrderStatus.Processing,
            "completed" => OrderStatus.Delivered,
            "canceled" or "cancelled" => OrderStatus.Cancelled,
            "fully refunded" or "partially refunded" => OrderStatus.Returned,
            _ => OrderStatus.Unknown,
        };
    }
}
