namespace Integration.TradeXpress.Orders;

/// <summary>Sipariş kalemi ile fiş satırı arasındaki bağın TÜRÜ — aynı kalem farklı aşamalarda farklı
/// satırlara bağlanır (rezerve → fiziki çıkış → iade).</summary>
public enum OrderFulfillmentLinkKind : byte
{
    /// <summary>Rezervasyon satırı (<c>ProcessPaymentType.Reservation</c>) — fiziksel Net'e girmez.</summary>
    Reservation = 0,

    /// <summary>Fiziki çıkış satırı — malın gerçekten çıktığı an.</summary>
    PhysicalExit = 1,

    /// <summary>İade giriş satırı — mal fiziksel olarak kasaya GİRDİĞİNDE yazılır; öncesinde stokta yok sayılır.</summary>
    Return = 2,
}
