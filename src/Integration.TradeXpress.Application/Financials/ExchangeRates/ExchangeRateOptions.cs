using System;
using System.Collections.Generic;

using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Financials.ExchangeRates;

/// <summary>
/// Harem feed (<see cref="HaremPlaywrightFeedWorker"/>) ayarları.
/// Config bölümü <c>"ExchangeRates"</c> — feed'in SAHİBİ Blazor host olduğundan bölüm
/// Blazor appsettings.json'dadır (keşif turu 2, O4: eskiden yanlışlıkla HttpApi.Host'taydı).
/// Harem-only (Altınkaynak devre dışı). Eski HaremBridge (HTTP köprü) anahtarları kaldırıldı.
/// </summary>
public sealed class ExchangeRateOptions
{
    public const string SectionName = "ExchangeRates";

    /// <summary>Feed poll/işleme sıklığı (cache tazeliği).</summary>
    public TimeSpan FetchInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>DB snapshot penceresi — host bu periyotta bir ExchangeRate yazar.</summary>
    public TimeSpan PersistInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Harem feed'ini açma/kapama.</summary>
    public bool HaremEnabled { get; set; } = true;

    /// <summary>Bir kotasyonun "taze" sayılacağı azami yaş; feed donarsa eski veri elenir.</summary>
    public TimeSpan HaremFreshness { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>DB snapshot'a yazılan birim kodları (seed edilen birimlerimiz).</summary>
    public HashSet<string> PersistedCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        CurrencyUnitCode.HAS, CurrencyUnitCode.GUM, CurrencyUnitCode.USD, CurrencyUnitCode.EUR,
        CurrencyUnitCode.GBP, CurrencyUnitCode.CHF, CurrencyUnitCode.SAR, CurrencyUnitCode.AUD,
        CurrencyUnitCode.CAD, CurrencyUnitCode.PLT, CurrencyUnitCode.PLD,
        // TRY pivot feed'den gelmez (seed'de 1/1).
    };

    // ── In-process Playwright feed (HaremBridge'i emekli etti; Node/Python/8765 gerekmez) ──

    /// <summary>Harem canlı piyasalar sayfası (socket.io WS bu sayfadan beslenir).</summary>
    public string HaremPageUrl { get; set; } = "https://canlipiyasalar.haremaltin.com/";

    /// <summary>Playwright tarayıcı channel'ı: null/boş = bundled Chromium (sistem tarayıcı gerekmez),
    /// "chrome" = kurulu Google Chrome, "msedge" = Edge. Varsayılan bundled.</summary>
    public string? BrowserChannel { get; set; }

    /// <summary>Tarayıcı görünmez mi (varsayılan true). Cloudflare soğuk headless'i geçiyor (test edildi).</summary>
    public bool Headless { get; set; } = true;

    /// <summary>Kalıcı tarayıcı profil klasörü (cf_clearance saklanır). Boşsa app data altında varsayılan.</summary>
    public string? BrowserProfileDir { get; set; }

    /// <summary>Bu süre boyunca hiç fiyat paketi gelmezse sayfa yeniden yüklenir (WS kopması/challenge kurtarma).</summary>
    public TimeSpan StaleReload { get; set; } = TimeSpan.FromSeconds(45);
}
