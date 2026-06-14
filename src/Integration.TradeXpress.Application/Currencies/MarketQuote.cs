using System;

namespace Integration.TradeXpress.Currencies;

/// <summary>Feed'ten gelen tek enstrümanın bellek-içi anlık kotasyonu (ham piyasa).</summary>
public class MarketQuote
{
    /// <summary>İç birim kodu (HAS, USD, ...) — HaremCodeMapping ile eşlenmiş.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Kaynak sembol (ALTIN, USDTRY, ...) — bilgi amaçlı.</summary>
    public string Description { get; set; } = string.Empty;

    public decimal Buy { get; set; }
    public decimal Sell { get; set; }

    /// <summary>Feed'in ham "tarih" string'i (TR yerel).</summary>
    public string UpdatedAtRaw { get; set; } = string.Empty;

    /// <summary>UTC'ye çevrilmiş timestamp; parse başarısızsa null (bayatlık guard'ı bayat sayar).</summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>Kotasyonun kaynağı ("Harem").</summary>
    public string Source { get; set; } = string.Empty;
}
