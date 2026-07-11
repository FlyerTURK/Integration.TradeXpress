namespace Integration.TradeXpress.Orders;

/// <summary>
/// NÖTR sipariş durumu — TÜM satış kanallarının ham durumları bu tek çarka eşlenir (kanal-agnostik ortak panel
/// filtresi + görüntüsü). Ham kanal durumu ayrıca <c>Order.RemoteStatus</c>'te string olarak saklanır; bu enum
/// yalnız kanallar-arası ortak yorumdur. Eşleme saf statik yardımcıda (ör. <c>TrendyolOrderStatusMapper</c>).
/// </summary>
public enum OrderStatus : byte
{
    /// <summary>Çözülemeyen / henüz eşlenmemiş ham durum (sessiz "New" varsaymak yerine belirsizliği taşır).</summary>
    Unknown = 0,

    /// <summary>Yeni sipariş — henüz işleme alınmadı.</summary>
    New = 1,

    /// <summary>İşleniyor — hazırlanıyor / faturalanıyor / paketleniyor.</summary>
    Processing = 2,

    /// <summary>Kargoya verildi.</summary>
    Shipped = 3,

    /// <summary>Teslim edildi.</summary>
    Delivered = 4,

    /// <summary>İptal edildi.</summary>
    Cancelled = 5,

    /// <summary>İade edildi / teslim edilemedi.</summary>
    Returned = 6,
}
