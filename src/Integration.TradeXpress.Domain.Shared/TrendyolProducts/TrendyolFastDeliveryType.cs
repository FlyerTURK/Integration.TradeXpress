namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol hızlı teslimat tipi (<c>deliveryOption.fastDeliveryType</c>). Kullanılırsa
/// <c>deliveryDuration=1</c> ZORUNLUdur (Trendyol V2 kuralı). Wire'a enum ADI yazılır.
/// </summary>
public enum TrendyolFastDeliveryType : byte
{
    /// <summary>Aynı gün kargo.</summary>
    SameDayShipping = 1,

    /// <summary>Hızlı teslimat.</summary>
    FastDelivery = 2,
}
