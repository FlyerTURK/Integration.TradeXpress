using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Volo.Abp.MultiTenancy;
using Integration.TradeXpress.Currencies;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Blazor.Client.Pages.Currencies.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Currencies;

public partial class CurrencyUnitListPage : IDisposable
{
    public CurrencyUnitListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    /// <summary>"Margin Ayarla" action'ının açtığı yeniden kullanılabilir diyalog.</summary>
    protected MarginSetDialog? MarginDialog;



    /// <summary>Host tenant değilse (bir tenant seçiliyse) true döner.</summary>
    protected bool IsTenantMode => CurrentTenant?.Id != null;

    [Inject]
    protected ICurrencyUnitAppService CurrencyUnitAppService { get; set; } = default!;

    [Inject]
    protected IEffectivePriceAppService PriceAppService { get; set; } = default!;

    // Birim Id → güncel efektif fiyat (canlı). Worker cache'ini ~8 sn'de bir okur.
    private readonly Dictionary<Guid, CurrentPriceDto> _live = new();
    private CancellationTokenSource? _priceCts;

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
            _live.Clear();
            foreach (var p in prices) _live[p.Id] = p;
        }
        catch { /* feed yoksa sessiz geç — liste yine de çalışır */ }
    }

    private async Task LivePriceLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(8));
            while (await timer.WaitForNextTickAsync(ct))
            {
                await LoadLivePricesAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { /* sayfa kapandı */ }
    }

    /// <summary>Satır için canlı Alış/Satış metni; fiyat yoksa "—".</summary>
    protected string LivePrice(Guid unitId, bool buy)
        => _live.TryGetValue(unitId, out var p)
            ? (buy ? p.Buy : p.Sell).ToString("N4")
            : "—";

    void IDisposable.Dispose()
    {
        _priceCts?.Cancel();
        _priceCts?.Dispose();
        base.Dispose();
    }

    public override Volo.Abp.Application.Services.ICrudAppService<
        CurrencyUnitGetDto, CurrencyUnitListDto, Guid,
        CurrencyUnitListRequestDto, CurrencyUnitCreateDto, CurrencyUnitUpdateDto> CrudAppService
        => CurrencyUnitAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.CurrencyUnits.Default;

    public override async Task BeforeUpdateAsync(CurrencyUnitListDto entity)
    {
        if (entity.IsGlobal && CurrentTenant.Id != null)
        {
            UiService.ShowWarningToast(L["TradeXpress:CurrencyUnit:CannotEditGlobalAsTenant"]);
            return;
        }
        await base.BeforeUpdateAsync(entity);
    }

    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<CurrencyUnitListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:CurrencyUnit:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }

        public override System.Type EditComponentType => typeof(Integration.TradeXpress.Blazor.Client.Pages.Currencies.CurrencyUnitEditPage);
    }


