namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// Bir piyasa fiyatına uygulanan tek margin kuralı (value object): tip + değer.
/// Türetme matematiği (4 <see cref="MarginType"/> dalı) <b>burada</b> kapsüllenir —
/// hesaplama her yere dağılmaz, tek yerden test edilir. Immutable.
/// </summary>
public class MarginSetting : ValueObject
{
    public MarginType Type { get; }
    public decimal Value { get; }

    private MarginSetting() { } // EF / serialization

    public MarginSetting(MarginType type, decimal value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>No-op margin: market fiyatını olduğu gibi geçirir (×1).
    /// Her erişimde YENİ instance döner — EF owned-type tracker iki owned navigation'a
    /// (MarginOnBuy/MarginOnSell) aynı instance'ı paylaştıramaz (immutable olsa da reference identity izler).</summary>
    public static MarginSetting Passthrough => new(MarginType.Multiply, 1m);

    /// <summary>Sabit fiyat margin'i (feed'i yok say).</summary>
    public static MarginSetting Fixed(decimal price) => new(MarginType.FinalPrice, price);

    /// <summary>Verilen piyasa fiyatından nihai fiyatı türetir.</summary>
    public decimal Apply(decimal marketPrice) => Type switch
    {
        MarginType.FinalPrice => Value,
        MarginType.Multiply   => marketPrice * Value,
        MarginType.Amount     => marketPrice + Value,
        MarginType.Percent    => marketPrice * (1m + Value / 100m),
        _                     => marketPrice
    };

    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return Type;
        yield return Value;
    }
}
