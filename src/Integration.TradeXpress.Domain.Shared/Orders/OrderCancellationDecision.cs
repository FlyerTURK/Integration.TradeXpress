namespace Integration.TradeXpress.Orders;

/// <summary>
/// Siparişin İPTAL ekseni — insan kararı (2026-08-05 Hakan kararı: <i>"Alınmış sipariş henüz kargolanmamışsa
/// dahi otomatik iptal olmamalı. Kullanıcı onayından ya da reddinden geçmeli."</i>).
/// <para><b>HİÇBİR İPTAL OTOMATİK DEĞİLDİR.</b> Kanaldan gelen iptal talebi bu ekseni
/// <see cref="Pending"/>'e alır ve orada BEKLER; stok ekseni (<see cref="OrderReservationStatus"/>)
/// dokunulmadan kalır, yani maden tutulmaya devam eder. Gerekçe: fiş kesilmemiş olsa bile mal fiziksel olarak
/// hazırlanmış/kesilmiş/eritilmiş olabilir — bunu yalnız kullanıcı bilir.</para>
/// <para>Kıymetli maden yasal olarak iptal/iadeye kapalıdır; sistem yine de DESTEKLER ve yalnız UYARIR —
/// <i>"reddetme zevkini bana bırak"</i>. Mekanizma ≠ politika.</para>
/// </summary>
public enum OrderCancellationDecision : byte
{
    /// <summary>İptal talebi yok.</summary>
    None = 0,

    /// <summary>Kanaldan iptal talebi geldi, KARAR BEKLİYOR. Rezervasyon serbest BIRAKILMAZ.</summary>
    Pending = 1,

    /// <summary>Kullanıcı iptali ONAYLADI → rezervasyon serbest bırakılır.</summary>
    Approved = 2,

    /// <summary>Kullanıcı iptali REDDETTİ → rezervasyon tutulmaya devam eder.</summary>
    Rejected = 3,
}
