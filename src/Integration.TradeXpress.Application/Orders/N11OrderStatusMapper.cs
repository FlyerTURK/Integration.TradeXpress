using System.Globalization;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// N11 ham sipariş durumu (order.status — <c>xs:integer</c>) → NÖTR <see cref="OrderStatus"/> eşlemesi
/// (SAF STATİK; birim testli). Ham durum ayrıca <c>Order.RemoteStatus</c>'te saklanır (bilgi kaybı yok); insan-okunur
/// N11 etiketi için bkz. <see cref="N11OrderStatusCatalog"/>.
/// <para><b>Kod tablosu N11 SOAP Referans Dokümantasyonu v4.6'dan (GROUND TRUTH):</b> order.status yalnız 5 kaba durum
/// döner (kalem-durumu daha zengindir — bkz. katalog). <see cref="OrderStatus.Unknown"/> yalnız çözülemeyen/geçersiz
/// koda düşer (ham kod RemoteStatus'te korunur).</para>
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

    /// <summary>N11 order.status kodlarını nötr duruma eşler (SOAP ref v4.6): 1 İşlem Bekliyor→New · 2 İşlemde→Processing ·
    /// 3 İptal Edilmiş→Cancelled · 4 Geçersiz→Unknown (belirsiz; ham korunur) · 5 Tamamlandı→Delivered (canlı gözlem
    /// 2017: kalem-status 10 + trackingNumber + shippingDate dolu). N11 order-status yalnız bu 5 kaba durumu döner;
    /// zengin kalem-durumu ayrıdır (bkz. <see cref="N11OrderStatusCatalog"/>).</summary>
    private static OrderStatus MapCode(int code)
    {
        return code switch
        {
            1 => OrderStatus.New,           // İşlem Bekliyor
            2 => OrderStatus.Processing,    // İşlemde
            3 => OrderStatus.Cancelled,     // İptal Edilmiş
            5 => OrderStatus.Delivered,     // Tamamlandı
            _ => OrderStatus.Unknown,       // 4 Geçersiz + bilinmeyen → belirsiz (ham RemoteStatus korunur)
        };
    }
}
