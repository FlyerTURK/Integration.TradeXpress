namespace Integration.TradeXpress.Orders;

/// <summary>
/// Siparişin STOK ekseni — malın fiziksel yolculuğu (2026-08-05 Hakan kararları).
/// <para><b>İptal ekseninden AYRI</b> (<see cref="OrderCancellationDecision"/>): kanaldan iptal talebi gelmesi
/// malın hazırlanmamış olduğu anlamına GELMEZ — mal kesilmiş/eritilmiş olabilir. Bu yüzden iptal talebi bu
/// ekseni KENDİLİĞİNDEN hareket ettirmez; rezervasyon karar verilene kadar TUTULUR.</para>
/// <para><b>⛔ ZAMAN AŞIMI KAVRAMI YOKTUR</b> — "sipariş siparıştir". Bu enum'a <c>Expired</c> benzeri bir
/// değer eklemek yasaktır (<c>OrderReservationConventionTests</c> mekanik olarak engeller).</para>
/// </summary>
public enum OrderReservationStatus : byte
{
    /// <summary>Maden/mamül müşteriye AYRILDI — fiziksel Net'e girmez, kullanılabilirden düşer.</summary>
    Reserved = 0,

    /// <summary>Fiziki çıkış yapıldı — rezervasyon serbest bırakıldı ve yerine gerçek çıkış fişi geçti.
    /// <para>Dönüşü olmayan nokta budur: bundan sonrası iptal değil İADE sürecidir.</para></summary>
    Fulfilled = 1,

    /// <summary>Rezervasyon serbest bırakıldı (iptal onaylandı ya da kalem eşleşmesi kaldırıldı) — stok
    /// yeniden satılabilir. Fiş satırı soft-delete'lidir: sayaçtan düşer, denetim izi kalır.</summary>
    Released = 2,

    /// <summary>Rezervasyon KURULAMADI — reçete çözülemedi, kalem yerel varyanta eşleşmedi ya da fiş
    /// yazılamadı. <b>Sessiz atlama DEĞİL:</b> gelen kutusunda görünür ve operatör elle bağlar.</summary>
    Blocked = 3,
}
