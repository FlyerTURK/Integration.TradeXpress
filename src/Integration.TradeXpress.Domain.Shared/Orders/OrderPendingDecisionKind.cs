namespace Integration.TradeXpress.Orders;

/// <summary>
/// "Karar Bekleyenler" sekmesindeki satırın NEDEN listede olduğu — görünüm discriminator'ı (2026-08-21 Hakan
/// yerleşim kararı). Üç kaynak TEK listede toplanır, tip başına ayrı uç AÇILMAZ; ayrımı DTO'daki bu alan yapar.
/// <para><b>Öncelik sırası</b> (bir kayıt birden çok ölçüte uyarsa): iptal talebi &gt; kurulamayan rezervasyon &gt;
/// yaşlanan rezerv — rozeti, kullanıcıdan İŞ isteyen en acil durum belirler. Ham iki eksen
/// (<see cref="OrderReservationStatus"/> + <see cref="OrderCancellationDecision"/>) DTO'da ayrıca taşınır;
/// rozet bilgiyi gizlemez, yalnız önceliklendirir.</para>
/// </summary>
public enum OrderPendingDecisionKind : byte
{
    /// <summary>Kanaldan iptal talebi geldi, İNSAN kararı bekliyor — rezervasyon kendiliğinden BIRAKILMAZ
    /// (hiçbir iptal otomatik değildir; mal hazırlanmış/kesilmiş olabilir, bunu yalnız kullanıcı bilir).</summary>
    CancellationRequested = 0,

    /// <summary>Rezervasyon KURULAMADI (eşleşmeyen kalem / reçetesiz ürün / fiş yazılamadı) — fail-closed
    /// <see cref="OrderReservationStatus.Blocked"/>. Sessiz atlama değildir: operatör elle bağlayana kadar
    /// bu listede görünür.</summary>
    BlockedReservation = 1,

    /// <summary>Yaş eşiğini aşan AKTİF rezerv — süre AŞIMI DEĞİL (⛔ zaman aşımı yok: "sipariş siparıştir",
    /// rezervasyon kendiliğinden bırakılmaz). Yalnız GÖRÜNÜRLÜK: unutulmuş sipariş kendini göstersin.</summary>
    AgingReservation = 2,
}
