using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Blazor.Client.Pages.Financials.CurrencyUnits.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Financials.CurrencyUnits;

public partial class CurrencyUnitListPage : IDisposable
{
    public CurrencyUnitListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    /// <summary>"Margin Ayarla" action'ının açtığı yeniden kullanılabilir diyalog.</summary>
    protected MarginSetDialog? MarginDialog;

    /// <summary>Toolbar custom action(lar)ı — "Marj Ayarla" (SortIndex 300: Sil ile Arama arası).</summary>
    private IReadOnlyList<Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction> MarginActions => new[]
    {
        new Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction
        {
            SortIndex = 300,
            Text = L["SetMargin"],
            Tooltip = L["SetMargin"],
            IconCssClass = TradeXpressIcons.CurrencyMargin,
            Enabled = StateService.SelectedDataItems?.Count == 1,
            OnClick = OpenMarginDialogAsync,
        },
    };

    /// <summary>Toolbar "Marj Ayarla" — tek satır seçiliyse diyaloğu açar.</summary>
    private async Task OpenMarginDialogAsync()
    {
        var item = StateService.SelectedDataItems?.Count == 1
            ? StateService.SelectedDataItems[0] as CurrencyUnitListDto
            : null;
        if (item != null && MarginDialog != null)
        {
            var (baseBuy, baseSell) = _live.TryGetValue(item.Id, out var p) ? (p.RawBuy, p.RawSell) : (0m, 0m);
            await MarginDialog.ShowAsync(item.Id, item.Code, item.Name, baseBuy, baseSell);
        }
    }



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

    /// <summary>Satır için canlı efektif Alış/Satış metni; fiyat yoksa "—".</summary>
    protected string LivePrice(Guid unitId, bool buy)
        => _live.TryGetValue(unitId, out var p)
            ? (buy ? p.Buy : p.Sell).ToString("N5")
            : "—";

    /// <summary>Baz (ham pivot) Alış/Satış fiyatı.</summary>
    protected string LiveRaw(Guid unitId, bool buy)
        => _live.TryGetValue(unitId, out var p)
            ? (buy ? p.RawBuy : p.RawSell).ToString("N5")
            : "—";

    /// <summary>Baz fiyata uygulanan marj tipi (alış/satış) — lokalize (Enum:MarginType:*).</summary>
    protected string LiveMarginType(Guid unitId, bool buy)
        => _live.TryGetValue(unitId, out var p)
            ? L[$"Enum:MarginType:{(buy ? p.MarginOnBuyType : p.MarginOnSellType)}"]
            : "—";

    /// <summary>Baz fiyata uygulanan marj değeri (alış/satış).</summary>
    protected string LiveMarginValue(Guid unitId, bool buy)
        => _live.TryGetValue(unitId, out var p)
            ? (buy ? p.MarginOnBuyValue : p.MarginOnSellValue).ToString("N5")
            : "—";

    /// <summary>Takip marj tipini lokalize gösterir (Enum:MarginType:*); takip yoksa boş.</summary>
    protected string FollowingMarginTypeText(MarginType? type)
        => type is { } t ? L[$"Enum:MarginType:{t}"].Value : "";

    /// <summary>Takip edilen birimin (varsa) tenant'ın gördüğü efektif Alış/Satış fiyatı (ayrı kolon); takip/fiyat yoksa boş.</summary>
    protected string FollowedPrice(Guid? followingUnitId, bool buy)
        => followingUnitId is { } pid && _live.TryGetValue(pid, out var p)
            ? (buy ? p.Buy : p.Sell).ToString("N5")
            : "";

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

    // Global (host) birimi tenant'ta engellemiyoruz; salt-okunur olarak AÇILIR (CurrencyUnitEditPage.IsReadOnly
    // banner + devre dışı form sağlar). Düzenleme/silme zaten hem UI hem server tarafında bloklu.
    public override async Task BeforeUpdateAsync(CurrencyUnitListDto entity)
        => await base.BeforeUpdateAsync(entity);

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

    // Edit'i popup yerine MDI sekmesinde aç (route'lu CurrencyUnitEditPage + IMdiTabOpener üzerinden).
    protected override Integration.Framework.Blazor.Client.Components.Crud.EditOpenTarget EditOpenTarget
        => Integration.Framework.Blazor.Client.Components.Crud.EditOpenTarget.MdiTab;

    public override System.Type EditComponentType => typeof(Integration.TradeXpress.Blazor.Client.Pages.Financials.CurrencyUnits.CurrencyUnitEditPage);
}


