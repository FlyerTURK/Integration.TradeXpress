using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Bullions;

/// <summary>
/// Takoz (külçe) ana metal türü — ERPPRO <c>Sistem.TakozTipleri</c> (sabit 4 satır:
/// ALTIN/GUMUS/PLATIN/PALLADIUM TAKOZU) enum karşılığı. Değerler legacy Id'lerle hizalı (1'den başlar).
/// Ana birim eşlemesi <see cref="BullionTypeExtensions.MainUnitCode"/> ile statik.
/// </summary>
public enum BullionType : byte
{
    /// <summary>Altın takozu — ana birim HAS.</summary>
    Gold      = 1,

    /// <summary>Gümüş takozu — ana birim GUM.</summary>
    Silver    = 2,

    /// <summary>Platin takozu — ana birim PLT.</summary>
    Platinum  = 3,

    /// <summary>Paladyum takozu — ana birim PLD.</summary>
    Palladium = 4,
}

/// <summary>Takoz türü → kanonik ana birim kodu eşlemesi (<see cref="CurrencyUnitCode"/> sabitleri).</summary>
public static class BullionTypeExtensions
{
    public static string MainUnitCode(this BullionType type) => type switch
    {
        BullionType.Silver    => CurrencyUnitCode.GUM,
        BullionType.Platinum  => CurrencyUnitCode.PLT,
        BullionType.Palladium => CurrencyUnitCode.PLD,
        _                     => CurrencyUnitCode.HAS,
    };
}
