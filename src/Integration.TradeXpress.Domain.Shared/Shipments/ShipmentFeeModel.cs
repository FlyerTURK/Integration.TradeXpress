namespace Integration.TradeXpress.Shipments;

/// <summary>Kargo ücret modeli (kanal-nötr çekirdek). Kanallar bu modeli kendi kodlamalarına eşler
/// (ör. N11 <c>deliveryFeeType</c>). Enum 1'den başlar (CLR default 0 geçersiz → fail-fast).</summary>
public enum ShipmentFeeModel : byte
{
    /// <summary>Ücretsiz kargo — alıcı ödemez (satıcı/mağaza karşılar).</summary>
    Free = 1,

    /// <summary>Alıcı öder.</summary>
    BuyerPays = 2,

    /// <summary>Şartlı — belirli eşik (tutar/adet) üzeri ücretsiz. <c>ConditionalThreshold</c> + <c>ConditionalUnit</c> zorunlu.</summary>
    Conditional = 3,
}
