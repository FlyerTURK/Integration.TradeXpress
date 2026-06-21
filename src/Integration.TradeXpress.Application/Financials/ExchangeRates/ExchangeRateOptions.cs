using System;
using System.Collections.Generic;

using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Financials.ExchangeRates;

/// <summary>
/// <see cref="ExchangeRateFeedWorker"/> + <see cref="HaremClient"/> ayarları.
/// Config bölümü <c>"ExchangeRates"</c> (HttpApi.Host appsettings.json).
/// Harem-only (Altınkaynak devre dışı). v1 polling — SSE push sonra eklenecek.
/// </summary>
public sealed class ExchangeRateOptions
{
    public const string SectionName = "ExchangeRates";

    /// <summary>Worker'ın köprüyü poll etme sıklığı (cache tazeliği).</summary>
    public TimeSpan FetchInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>DB snapshot penceresi — host bu periyotta bir ExchangeRate yazar.</summary>
    public TimeSpan PersistInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Harem feed'ini (HaremBridge) açma/kapama. Kapalıyken köprüye gidilmez.</summary>
    public bool HaremEnabled { get; set; } = true;

    /// <summary>HaremBridge JSON endpoint'i.</summary>
    public string HaremBridgeUrl { get; set; } = "http://127.0.0.1:8765/";

    /// <summary>HaremBridge HTTP timeout'u (localhost; kısa).</summary>
    public TimeSpan HaremHttpTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Bir kotasyonun "taze" sayılacağı azami yaş; köprü donarsa eski veri elenir.</summary>
    public TimeSpan HaremFreshness { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>DB snapshot'a yazılan birim kodları (seed edilen birimlerimiz).</summary>
    public HashSet<string> PersistedCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        CurrencyUnitCode.HAS, CurrencyUnitCode.GUM, CurrencyUnitCode.USD, CurrencyUnitCode.EUR,
        CurrencyUnitCode.GBP, CurrencyUnitCode.CHF, CurrencyUnitCode.SAR, CurrencyUnitCode.AUD,
        CurrencyUnitCode.CAD, CurrencyUnitCode.PLT, CurrencyUnitCode.PLD,
        // TRY pivot feed'den gelmez (seed'de 1/1).
    };

    /// <summary>Ardışık hata log aralığı: 1. ve her N'inci hatada logla.</summary>
    public int LogEveryNthFailure { get; set; } = 60;
}
