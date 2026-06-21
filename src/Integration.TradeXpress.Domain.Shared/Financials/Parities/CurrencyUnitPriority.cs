using System;
using System.Collections.Generic;
using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Çift (Parity) yön konvansiyonu: düşük index = yüksek öncelik = çiftin <b>BASE</b>'i.
/// Forex önceliğine uyarlı: metaller (XAU/XAG/Pt/Pd) > majörler (EUR>GBP>AUD) > USD >
/// minörler > pivot TRY (en düşük). Örn. USD+TRY→USDTRY (USD base), EUR+USD→EURUSD.
/// </summary>
public static class CurrencyUnitPriority
{
    private static readonly string[] Order =
    {
        CurrencyUnitCode.HAS, CurrencyUnitCode.GUM, CurrencyUnitCode.PLT, CurrencyUnitCode.PLD,
        CurrencyUnitCode.EUR, CurrencyUnitCode.GBP, CurrencyUnitCode.AUD, CurrencyUnitCode.USD,
        CurrencyUnitCode.CAD, CurrencyUnitCode.CHF, CurrencyUnitCode.SAR, CurrencyUnitCode.TRY,
    };

    /// <summary>Kod'un öncelik sırası (küçük = güçlü/base). Bilinmeyen kod en sona düşer.</summary>
    public static int RankOf(string code)
    {
        var i = Array.FindIndex(Order, c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? int.MaxValue : i;
    }

    /// <summary>İki koddan hangisi çiftin base'i: önceliği yüksek (rank küçük) olan.</summary>
    public static (string Base, string Quote) Direct(string a, string b)
        => RankOf(a) <= RankOf(b) ? (a, b) : (b, a);

    /// <summary>Bilinen kodlar (öncelik sırasında) — seed kombinasyonu için.</summary>
    public static IReadOnlyList<string> KnownCodes => Order;
}
