using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

// Canlı kur bölümü — PeriodicTimer döngüsü, kur tazeleme, yön takibi (flash animasyonu) ve dispose.
// CurrentTransactionForm'un davranışsal parçası; okunabilirlik için ayrı partial dosyada.
public partial class CurrentTransactionForm
{
    private List<CurrentPriceDto> _liveRates = new();
    private Dictionary<Guid, decimal> _pivotBuy = new();   // konsolide bakiye için TUTARLI pivot Buy (parite görüntü değil)
    private PeriodicTimer? _rateTimer;
    private CancellationTokenSource? _rateCts;   // dispose'da döngüyü iptal eder (tick↔dispose yarış penceresi)
    private DateTime _lastRateChangeUtc;         // render kapısı: flash animasyonu penceresi (son değişim anı)

    private readonly Dictionary<(Guid Id, bool Buy), decimal> _prevEffective = new();
    private readonly Dictionary<(Guid Id, bool Buy), (int Dir, DateTime Until)> _flash = new();

    private void TrackDirection(Guid id, bool buy, decimal value, DateTime now)
    {
        var key = (id, buy);
        if (_prevEffective.TryGetValue(key, out var prev) && value != prev)
            _flash[key] = (value > prev ? 1 : -1, now.AddSeconds(1));
        _prevEffective[key] = value;
    }

    protected string PriceCellStyle(Guid id, bool buy)
    {
        var on = _flash.TryGetValue((id, buy), out var f) && DateTime.UtcNow < f.Until;
        var bg = !on ? "transparent"
            : f.Dir > 0
                ? "var(--flash-green)"
                : "var(--flash-red)";
        return $"display:block; text-align:right; padding:2px 6px; border-radius:4px; background:{bg}; transition: background 700ms ease-out;";
    }

    private async Task LiveRateLoopAsync()
    {
        _rateCts   = new CancellationTokenSource();
        _rateTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await _rateTimer.WaitForNextTickAsync(_rateCts.Token))
            {
                var changed = await RefreshRatesAsync();
                // Render kapısı: kur değişmediyse (ve flash animasyon penceresi kapandıysa) tüm form
                // ağacını her saniye yeniden çizme — büyük grid'lerde gereksiz diff yükü.
                if (changed || DateTime.UtcNow - _lastRateChangeUtc < TimeSpan.FromSeconds(1.5))
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }   // tick ↔ dispose yarış penceresi (circuit kapanışı) — sessiz çık
    }

    /// <summary>Kurları tazeler; en az bir fiyat DEĞİŞTİYSE true döner (render kapısı için).</summary>
    private async Task<bool> RefreshRatesAsync()
    {
        var changed = false;
        try
        {
            var prices = await PriceService.GetCurrentPricesAsync();   // YEREL para birimine re-base'li (ülke parası); bilanço değil
            var now = DateTime.UtcNow;
            foreach (var p in prices)
            {
                var old = _liveRates.FirstOrDefault(x => x.Id == p.Id);
                if (old is null || old.Buy != p.Buy || old.Sell != p.Sell)
                {
                    changed = true;
                }
                TrackDirection(p.Id, buy: true, p.Buy, now);
                TrackDirection(p.Id, buy: false, p.Sell, now);
            }
            _liveRates = prices;
            // Konsolide bakiye matematiği aynı fiyat listesinden — İKİNCİ servis çağrısı GEREKMEZ
            // (eski kod aynı metodu tik başına iki kez çağırıyordu; canlı yol pahalı — teke indirildi).
            _pivotBuy = prices.ToDictionary(p => p.Id, p => p.Buy);
            if (changed)
            {
                _lastRateChangeUtc = now;
            }
        }
        catch { }
        ComputeConsolidated();   // konsolide toplam canlı kurla tazelensin
        return changed;
    }

    public void Dispose()
    {
        _rateCts?.Cancel();
        _rateCts?.Dispose();
        _rateTimer?.Dispose();
    }
}
