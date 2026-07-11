using System.Globalization;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// N11 ham sipariş durumu (DetailedOrderData.status — <c>xs:integer</c>) → NÖTR <see cref="OrderStatus"/> eşlemesi
/// (SAF STATİK; birim testli). Ham durum ayrıca <c>Order.RemoteStatus</c>'te saklanır (bilgi kaybı yok).
/// <para><b>DİKKAT — integer→anlam eşlemesi CANLI DOĞRULANMADI:</b> N11 sipariş durum tam sayılarının anlamı kamuya
/// net dokümante değil ve elimizde canlı sipariş yok. Yanlış eşleme (ör. iptali "Teslim" göstermek) yanıltıcı
/// olacağından ihtiyatlı davranılır: bilinmeyen değer <see cref="OrderStatus.Unknown"/>'a düşer, ham kod korunur.
/// İlk gerçek siparişler görüldüğünde <see cref="Map"/> gövdesine bilinen kodlar eklenerek rafine edilir (tek yer).</para>
/// </summary>
public static class N11OrderStatusMapper
{
    /// <summary>Ham N11 durum metnini (integer string) nötr duruma çevirir; boş/bilinmeyen → Unknown.</summary>
    public static OrderStatus Map(string? remoteStatus)
    {
        if (!int.TryParse(remoteStatus, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return OrderStatus.Unknown;
        }

        return MapCode(code);
    }

    /// <summary>Bilinen N11 sipariş durum kodlarını nötr duruma eşler. Canlı doğrulandı (2026-07-11): order-status
    /// <b>5</b> = tamamlanmış/teslim (kalem-status 10; trackingNumber + shippingDate dolu 2017 siparişleri) → Delivered.
    /// Diğer kodlar henüz gözlenmedi → Unknown (sessizce varsayMAZ; ham değer RemoteStatus'te korunur, yeni kod
    /// görüldükçe buraya eklenir).</summary>
    private static OrderStatus MapCode(int code)
    {
        return code switch
        {
            5 => OrderStatus.Delivered,
            _ => OrderStatus.Unknown,
        };
    }
}
