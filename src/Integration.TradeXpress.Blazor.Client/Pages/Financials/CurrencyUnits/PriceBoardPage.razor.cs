using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Blazor.Client.Pages.Financials.CurrencyUnits.Components;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Financials.CurrencyUnits;

/// <summary>
/// Kur panosu — viewer'ın GÜNCEL efektif fiyatları (ham pivot × kademe). Her satırda
/// "Margin Ayarla" (append-only SetAsync) ve marj "Geçmiş" aksiyonları.
/// (Parite cross-pairs + forex yön bayrağı Parity increment'iyle gelecek.)
/// </summary>
public partial class PriceBoardPage
{
    public PriceBoardPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Inject] protected IEffectivePriceAppService PriceAppService { get; set; } = default!;
    [Inject] protected ICurrencyUnitMarginAppService MarginAppService { get; set; } = default!;

    protected MarginSetDialog? MarginDialog;

    protected List<CurrentPriceDto> Prices { get; set; } = new();

    /// <summary>En güncel ham fiyatın zamanı (canlı/donmuş rozeti için).</summary>
    protected DateTime? LastUpdate => Prices.Count == 0 ? null : Prices.Max(p => p.RateDate);
    /// <summary>Son fiyat 2 dk içindeyse "canlı", değilse "donmuş" (hafta sonu/kapanış).</summary>
    protected bool IsLive => LastUpdate.HasValue && (DateTime.UtcNow - LastUpdate.Value) < TimeSpan.FromMinutes(2);

    protected bool HistoryVisible { get; set; }
    protected string HistoryTitle { get; set; } = string.Empty;
    protected List<CurrencyUnitMarginListDto> History { get; set; } = new();

    private PeriodicTimer? _timer;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        _ = AutoRefreshLoopAsync(); // canlı: her ~8s yeniden çek
    }

    private async Task AutoRefreshLoopAsync()
    {
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(8));
        try
        {
            while (await _timer.WaitForNextTickAsync())
            {
                await LoadAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { /* sayfa kapandı */ }
    }

    protected async Task LoadAsync()
    {
        Prices = await PriceAppService.GetCurrentPricesAsync();
    }

    public void Dispose() => _timer?.Dispose();

    protected async Task ShowHistoryAsync(CurrentPriceDto row)
    {
        HistoryTitle = $"{row.CurrencyUnitCode} — {L["History"]}";
        History = await MarginAppService.GetHistoryAsync(row.Id);
        HistoryVisible = true;
    }
}
