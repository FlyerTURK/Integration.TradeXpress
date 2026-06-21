using System;
using System.Collections.Generic;

using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Financials.ExchangeRates;

/// <summary>
/// Harem feed sembollerini (HaremBridge'in sunduğu ham kodlar) iç birim kodlarına
/// (<see cref="CurrencyUnitCode"/>) eşler. Harem dövizleri açık parite kodu kullanır
/// (USDTRY, EURTRY…), has altını ALTIN, gram gümüşü GUMUSTRY yayınlar.
///
/// <para>Yalnız Harem-güvenilir semboller listelenir. JPY/KWD (Harem bayat) ve
/// RUB/AZN/CNY/RON/AED (Harem vermez, Altınkaynak devre dışı) yoktur.
/// PLT/PLD düz geçiş DEĞİL — PLATIN/PALADYUM USD/kg cinsinden, gram-TRY türetimi
/// <see cref="HaremClient"/>'tedir. Bilinmeyen sembol → <c>null</c> (çağıran atlar).</para>
/// </summary>
public static class HaremCodeMapping
{
    private static readonly Dictionary<string, string> SymbolToUnit = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALTIN"]    = CurrencyUnitCode.HAS,   // Has altın (gram)
        ["GUMUSTRY"] = CurrencyUnitCode.GUM,   // Gram gümüş (TRY)
        ["USDTRY"]   = CurrencyUnitCode.USD,
        ["EURTRY"]   = CurrencyUnitCode.EUR,
        ["GBPTRY"]   = CurrencyUnitCode.GBP,
        ["CHFTRY"]   = CurrencyUnitCode.CHF,
        ["SARTRY"]   = CurrencyUnitCode.SAR,
        ["AUDTRY"]   = CurrencyUnitCode.AUD,
        ["CADTRY"]   = CurrencyUnitCode.CAD,
    };

    public static string? ToUnitCode(string haremSymbol)
        => SymbolToUnit.TryGetValue(haremSymbol, out var unit) ? unit : null;
}
