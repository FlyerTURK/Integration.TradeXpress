using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Upstream piyasa kotasyonlarının bellek-içi anlık görüntüsü. Singleton — tüm
/// tüketiciler (canlı-kur okuma, persist worker) aynı örneği paylaşır.
/// Quote.Code zaten iç birim kodumuzdur (HaremClient eşledi).
/// </summary>
public class ExchangeRateCacheService : ISingletonDependency
{
    private static readonly TimeSpan FlashWindow = TimeSpan.FromMilliseconds(3500);

    private readonly ConcurrentDictionary<string, MarketQuote> _current = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MarketQuote> _previous = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _flashUntil = new(StringComparer.OrdinalIgnoreCase);

    private DateTime _lastUpdated;
    public DateTime LastUpdated => _lastUpdated;

    /// <summary>Cache'e her yazışta tetiklenir (SSE stream "snapshot bayatladı" sinyali için).</summary>
    public event Action? Updated;

    public void UpdatePrices(IEnumerable<MarketQuote> quotes)
    {
        var now = DateTime.UtcNow;
        foreach (var quote in quotes)
        {
            var code = quote.Code;
            if (_current.TryGetValue(code, out var prev)
                && (prev.Buy != quote.Buy || prev.Sell != quote.Sell))
            {
                _previous[code] = prev;
                _flashUntil[code] = now + FlashWindow;
            }
            _current[code] = quote;
        }

        _lastUpdated = now;
        try { Updated?.Invoke(); } catch { /* sinyal best-effort */ }
    }

    public MarketQuote? GetByCode(string code)
        => _current.TryGetValue(code, out var quote) ? quote : null;

    public IReadOnlyDictionary<string, MarketQuote> GetAll() => _current;

    public PriceDirection GetBuyDirection(string code) => GetDirection(code, buy: true);
    public PriceDirection GetSellDirection(string code) => GetDirection(code, buy: false);

    private PriceDirection GetDirection(string code, bool buy)
    {
        if (!_flashUntil.TryGetValue(code, out var until) || DateTime.UtcNow > until)
            return PriceDirection.None;
        if (!_current.TryGetValue(code, out var current) || !_previous.TryGetValue(code, out var previous))
            return PriceDirection.None;

        var (cur, prev) = buy ? (current.Buy, previous.Buy) : (current.Sell, previous.Sell);
        if (cur > prev) return PriceDirection.Up;
        if (cur < prev) return PriceDirection.Down;
        return PriceDirection.None;
    }
}
