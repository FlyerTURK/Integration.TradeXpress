using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Volo.Abp.Application.Services;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Pages.Financials.Parities;

public partial class ParityListPage : IDisposable
{
    public ParityListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Inject]
    protected IParityAppService ParityAppService { get; set; } = default!;

    [Inject]
    protected IEffectivePriceAppService PriceAppService { get; set; } = default!;

    [Inject]
    protected Integration.TradeXpress.Blazor.Client.Services.Mdi.ITabManager TabManager { get; set; } = default!;

    /// <summary>Baz/Karşı kolonu linki → ilgili para biriminin edit'ini MDI sekmesinde aç.</summary>
    private async Task OpenUnitAsync(Guid unitId, string code)
    {
        if (unitId == Guid.Empty) return;
        await TabManager.OpenOrActivateAsync(
            $"/currencies/currency-units/{unitId}",
            $"{L["CurrencyUnit"]}: {code}",
            TradeXpressIcons.CurrencyUnit);
    }

    public override ICrudAppService<
        ParityGetDto, ParityListDto, Guid,
        ParityListRequestDto, ParityCreateDto, ParityUpdateDto> CrudAppService => ParityAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Parities.Default;

    protected override EditOpenTarget EditOpenTarget => EditOpenTarget.MdiTab;

    public override Type EditComponentType => typeof(ParityEditHost);

    // ── Canlı fiyat: birim Id → efektif alış/satış ──
    private readonly Dictionary<Guid, CurrentPriceDto> _live = new();
    private CancellationTokenSource? _priceCts;

    // Yön referansı + flash (yön + 1 sn pencere) — parite bazında
    private readonly Dictionary<Guid, decimal> _prevRate = new();
    private readonly Dictionary<Guid, (int Dir, DateTime Until)> _flash = new();

    // ParityRate() çağrıldığında her paritenin birim ID'leri buraya kaydedilir;
    // loop bu map üzerinde dolaşarak rate'i hesaplar (GridDataSource.CachedItems olmadan).
    private readonly Dictionary<Guid, (Guid BaseId, Guid QuoteId)> _parityUnits = new();

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        _priceCts = new CancellationTokenSource();
        await LoadLivePricesAsync();
        _ = LivePriceLoopAsync(_priceCts.Token);
    }

    private async Task LoadLivePricesAsync()
    {
        try
        {
            var prices = await PriceAppService.GetCurrentPricesAsync();
            var now = DateTime.UtcNow;
            _live.Clear();
            foreach (var p in prices) _live[p.Id] = p;

            // Flash takibi: _parityUnits'te kayıtlı her parite için Base.Buy / Quote.Buy oranı
            foreach (var (parityId, (baseId, quoteId)) in _parityUnits)
            {
                if (!_live.TryGetValue(baseId, out var b) || !_live.TryGetValue(quoteId, out var q) || q.Buy == 0m) continue;
                var rate = decimal.Round(b.Buy / q.Buy, 5);
                if (_prevRate.TryGetValue(parityId, out var prev) && rate != prev)
                    _flash[parityId] = (rate > prev ? 1 : -1, now.AddSeconds(1));
                _prevRate[parityId] = rate;
            }
        }
        catch { /* feed yoksa sessiz geç */ }
    }

    private async Task LivePriceLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(ct))
            {
                await LoadLivePricesAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Parite oranı: Base.Buy / Quote.Buy. Birim ID'lerini de kaydeder (flash loop için).</summary>
    protected decimal? ParityRate(ParityListDto p)
    {
        _parityUnits[p.Id] = (p.BaseCurrencyUnitId, p.QuoteCurrencyUnitId);
        if (!_live.TryGetValue(p.BaseCurrencyUnitId, out var b)) return null;
        if (!_live.TryGetValue(p.QuoteCurrencyUnitId, out var q)) return null;
        if (q.Buy == 0m) return null;
        return decimal.Round(b.Buy / q.Buy, 5);
    }

    /// <summary>Hücre stili: dikey gradyan flash, 700ms ease-out.</summary>
    protected string RateCellStyle(Guid parityId)
    {
        var on = _flash.TryGetValue(parityId, out var f) && DateTime.UtcNow < f.Until;
        var bg = !on ? "transparent"
            : f.Dir > 0
                ? "var(--flash-green)"
                : "var(--flash-red)";
        return $"display:block; text-align:right; padding:2px 6px; border-radius:4px; background:{bg}; transition: background 700ms ease-out;";
    }

    void IDisposable.Dispose()
    {
        _priceCts?.Cancel();
        _priceCts?.Dispose();
        base.Dispose();
    }

    public override async Task DeleteAsync()
    {
        var selected = StateService.SelectedDataItems;
        if (selected == null || selected.Count == 0)
            return;

        if (CurrentTenant.Id != null && selected.OfType<ParityListDto>().Any(x => x.IsGlobal))
        {
            UiService.ShowWarningToast(L["TradeXpress:Parity:CannotDeleteGlobalAsTenant"]);
            return;
        }

        await base.DeleteAsync();
    }
}
