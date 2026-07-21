namespace Integration.TradeXpress.Shipments;

/// <summary>Şartlı kargo eşiğinin birimi. <see cref="ShipmentFeeModel.Conditional"/> ile birlikte anlamlıdır.
/// Enum 1'den başlar (CLR default 0 geçersiz → fail-fast).</summary>
public enum ShipmentConditionalUnit : byte
{
    /// <summary>Tutar (para birimi) üzeri ücretsiz kargo.</summary>
    Amount = 1,

    /// <summary>Adet (miktar) üzeri ücretsiz kargo.</summary>
    Quantity = 2,
}
