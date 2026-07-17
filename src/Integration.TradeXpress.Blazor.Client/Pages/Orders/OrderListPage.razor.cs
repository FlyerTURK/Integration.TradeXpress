using System;
using System.Collections.Generic;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Blazor.Client.Pages.Orders;

/// <summary>
/// Ortak sipariş paneli — MASTER-DETAIL CrudLayout server-side grid. MASTER satır = SİPARİŞ (kanal/no/tarih/müşteri/
/// durum/tutar); genişletince DETAIL = o siparişin KALEMLERİ (nested grid). Sıralama: order status → tarih (yeni→eski).
/// Create/Delete YOK (Order yalnız senkronizasyondan gelir) — yalnız DÜZENLEME, STANDART CrudEditHost/EntityEditForm
/// akışıyla (OnUpdateClick → ViewOpener → OrderEditHost popup). "Siparişleri Çek" pazaryerinden (Trendyol + N11) çeker;
/// arka planda OrderSyncBackgroundWorker boş kanalları kendiliğinden doldurur. Bu düğme manuel/anında tazeleme.
/// </summary>
public partial class OrderListPage : IDisposable
{
    [Inject] protected IOrderAppService OrderAppService { get; set; } = default!;
    [Inject] public ICrudStateService<OrderListDto, Guid> StateService { get; set; } = default!;
    [Inject] protected IUiInteractionService UiService { get; set; } = default!;
    [Inject] protected IViewOpener ViewOpener { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private IReadOnlyList<CrudToolbarAction>? _customActions;
    private GridListDataSource<OrderListDto>? _gridDataSource;

    private bool _showReport;
    private OrderFetchResultDto? _result;

    /// <summary>Server-side grid kaynağı — <see cref="IOrderAppService.GetListAsync"/>'e bağlı (MASTER = sipariş).</summary>
    public GridListDataSource<OrderListDto> GridDataSource
        => _gridDataSource ??= new GridListDataSource<OrderListDto>(FetchPageAsync)
        { 
            OnError = ex => InvokeAsync(() => 
            {
                UiService?.ShowErrorToast(ex.Message);
                StateHasChanged();
            }) 
        };

    private Task<PagedResultDto<OrderListDto>> FetchPageAsync(ListRequestDto request)
    {
        var typed = new OrderListRequestDto
        {
            SkipCount      = request.SkipCount,
            MaxResultCount = request.MaxResultCount,
            Sorting        = request.Sorting,
            Filter         = request.Filter,
            Sorts          = request.Sorts,
            Filters        = request.Filters,
            IsActive       = request.IsActive,
        };
        return OrderAppService.GetListAsync(typed);
    }

    protected override Task OnInitializedAsync()
    {
        // Create/Delete YOK (Order yalnız senkronizasyondan gelir); DÜZENLEME var (Sipariş Fazı O1). Çekim ayrı custom action.
        StateService.IsGrantedCreate = StateService.IsGrantedDelete = false;
        StateService.IsGrantedUpdate = true;
        StateService.OnStateChanged += OnStateChangedHandler;

        _customActions = BuildCustomActions();
        return Task.CompletedTask;
    }

    // "Siparişleri Çek" — TÜM bağlı kanallardan (Trendyol + N11) manuel çeker (streaming). Worker zaten arka planda seed'ler.
    private IReadOnlyList<CrudToolbarAction> BuildCustomActions() => new List<CrudToolbarAction>
    {
        new()
        {
            SortIndex = 0,
            Text = L["Order:Fetch"],
            Tooltip = L["Order:Fetch:Tooltip"],
            IconCssClass = TradeXpressIcons.Download + " xaf-toolbar-item-icon",
            OnClick = FetchAsync,
        },
    };

    private async Task FetchAsync()
    {
        try
        {
            StateService.IsBusy = true;
            StateService.NotifyStateChanged();

            _result = await OrderAppService.FetchAllOrdersAsync();
            _showReport = true;
            UiService.ShowSuccessToast(L["Order:Fetch:Success"]);
            StateService.RequestReload();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
        finally
        {
            StateService.IsBusy = false;
            StateService.NotifyStateChanged();
        }
    }

    private string ReportSummary => _result is { } r
        ? string.Format(L["Order:Fetch:Summary"], r.FetchedOrders, r.NewOrders, r.UpdatedOrders, r.TotalLines, r.ChannelsProcessed)
        : string.Empty;

    private string ReportWindow => _result?.FetchedSinceUtc is { } since
        ? string.Format(L["Order:Fetch:Window"], since.ToString("yyyy-MM-dd"))
        : string.Empty;

    private string ReportIssues => _result is { } r ? string.Join(Environment.NewLine, r.Warnings) : string.Empty;

    // Satır tıklaması → STANDART edit akışı (Account/Branch ile AYNI): ViewOpener popup'ta OrderEditHost açar.
    // OnSaved → grid tazele; OnClosed → popup zaten ViewOpener/PopupService tarafından yönetilir.
    private Task OpenDetailAsync(OrderListDto row)
    {
        var extra = new Dictionary<string, object>
        {
            { "OnSaved", EventCallback.Factory.Create(this, ReloadAsync) },
        };
        return ViewOpener.OpenAsync(typeof(OrderEditHost), row.Id, string.Empty, TradeXpressIcons.SalesChannel, extra);
    }

    private string ChannelLabel(OrderListDto row) => ChannelText(row.ChannelType, row.SalesChannelCode);

    private string ChannelText(SalesChannelType type, string? code)
        => !string.IsNullOrWhiteSpace(code) ? code! : ChannelTypeLabel(type);

    private string ChannelTypeLabel(SalesChannelType type) => type switch
    {
        SalesChannelType.TrTrendyol => L["SalesChannelType:TrTrendyol"],
        SalesChannelType.Etsy => L["SalesChannelType:Etsy"],
        _ => L["SalesChannelType:TrN11"],
    };

    private string StatusLabel(OrderStatus status) => L[$"Enum:OrderStatus:{status}"];

    private Task ReloadAsync()
    {
        StateService.RequestReload();
        return Task.CompletedTask;
    }

    private Task OnSearchAsync(string text)
    {
        GridDataSource.SearchText = text;
        StateService.RequestReload();
        return Task.CompletedTask;
    }

    private async Task ShowErrorAsync(Exception ex)
    {
        UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
        await Task.CompletedTask;
    }

    private void OnStateChangedHandler() => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        if (StateService != null)
        {
            StateService.OnStateChanged -= OnStateChangedHandler;
        }
    }
}
