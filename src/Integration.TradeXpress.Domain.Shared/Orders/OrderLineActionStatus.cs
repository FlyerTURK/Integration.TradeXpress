namespace Integration.TradeXpress.Orders;

/// <summary>
/// Sipariş KALEMİNİN YEREL işlem durumu (Sipariş Fazı O2 — state machine) — N11'e YAZILAN eylemleri (kabul/red/
/// kargo) izler. N11'in kendi ham durum kodları (<see cref="N11OrderStatusCatalog"/>) AYRI ve BİZ YAZMADAN da
/// değişebilir (alıcı iptal talebi vb.); bu enum yalnız BİZİM tetiklediğimiz aksiyonların yerel izidir.
/// Geçişler <c>OrderLineOperationalData</c>'da guard'lı (Pending→Accepted|Rejected→(Accepted'ten)Shipped).
/// </summary>
public enum OrderLineActionStatus : byte
{
    /// <summary>Henüz kabul/red edilmedi — N11'e OrderItemAccept/Reject çağrısı yapılmadı.</summary>
    Pending = 0,

    /// <summary>N11'e OrderItemAccept ile bildirildi.</summary>
    Accepted = 1,

    /// <summary>N11'e OrderItemReject ile bildirildi (gerekçe <c>RejectReason</c>'da).</summary>
    Rejected = 2,

    /// <summary>N11'e MakeOrderItemShipment ile bildirildi.</summary>
    Shipped = 3,
}
