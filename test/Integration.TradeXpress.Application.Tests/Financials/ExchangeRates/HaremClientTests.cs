using System.Linq;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.ExchangeRates;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Financials.ExchangeRates;

/// <summary>HaremClient saf parse/map/türetme testleri — köprü/host gerekmez.</summary>
public class HaremClientTests
{
    private const string Sample = """
    {
      "lastUpdate": "2026-06-12T17:39:59+00:00",
      "kurlar": {
        "ALTIN":    { "alis": "6625.380", "satis": "6652.400", "tarih": "04-06-2026 17:39:59" },
        "USDTRY":   { "alis": "45.9180",  "satis": "45.9185",  "tarih": "04-06-2026 17:39:58" },
        "GUMUSTRY": { "alis": "852.50",   "satis": "862.75",   "tarih": "04-06-2026 17:39:59" },
        "PLATIN":   { "alis": "12450",    "satis": "12500",    "tarih": "04-06-2026 17:39:59" },
        "UNKNOWNX": { "alis": "1",        "satis": "2",        "tarih": "04-06-2026 17:39:59" }
      }
    }
    """;

    [Fact]
    public void Maps_known_symbols_and_skips_unknown()
    {
        var quotes = HaremClient.ParseSnapshot(Sample);

        quotes.Single(q => q.Code == CurrencyUnitCode.HAS).Buy.ShouldBe(6625.380m);
        quotes.Single(q => q.Code == CurrencyUnitCode.USD).Buy.ShouldBe(45.9180m);
        quotes.Single(q => q.Code == CurrencyUnitCode.GUM).Sell.ShouldBe(862.75m);
        quotes.ShouldNotContain(q => q.Code == "UNKNOWNX");
    }

    [Fact]
    public void Derives_platinum_from_usd_per_kg()
    {
        var quotes = HaremClient.ParseSnapshot(Sample);

        var plt = quotes.Single(q => q.Code == CurrencyUnitCode.PLT);
        // 12450/1000 × 45.9180 = 571.6791 ; 12500/1000 × 45.9185 = 573.9813 (round 4)
        plt.Buy.ShouldBe(571.6791m);
        plt.Sell.ShouldBe(573.9813m);
    }

    [Fact]
    public void Palladium_absent_when_leg_missing()
    {
        var quotes = HaremClient.ParseSnapshot(Sample);
        quotes.ShouldNotContain(q => q.Code == CurrencyUnitCode.PLD);
    }

    [Fact]
    public void Accepts_numeric_json_values()
    {
        // Harem bazı sembollerde alis/satis'i JSON number push eder.
        const string json = """
        { "kurlar": { "USDTRY": { "alis": 45.91, "satis": 45.92, "tarih": "04-06-2026 17:39:58" } } }
        """;
        var quotes = HaremClient.ParseSnapshot(json);
        quotes.Single(q => q.Code == CurrencyUnitCode.USD).Buy.ShouldBe(45.91m);
    }
}
