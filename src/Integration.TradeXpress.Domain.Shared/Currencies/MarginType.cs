namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Bir piyasa fiyatından nihai fiyatın nasıl türetileceğini belirler.
/// Hem feed düzeltmesi (host) hem follow markup'ı hem standalone fiyat için kullanılır.
/// </summary>
public enum MarginType
{
    /// <summary>final = market × Value. (Value=1 → no-op geçiş.)</summary>
    Multiply = 0,

    /// <summary>final = market × (1 + Value/100). Value yüzde markup'tır (2 → +%2).</summary>
    Percent = 1,

    /// <summary>final = market + Value. (Value mutlak ekleme/çıkarma.)</summary>
    Amount = 2,

    /// <summary>final = Value. Feed'i yok say, sabit fiyat (TRY=1, garbage override).</summary>
    FinalPrice = 3
}
