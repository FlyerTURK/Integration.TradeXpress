using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Blazor.Client.Pages.Orders;

/// <summary>
/// Ortak sipariş paneli (Sipariş Fazı O0) — TÜM kanalların siparişleri tek server-side grid'de (kanal yalnız
/// kolon/filtre). SALT-OKUMA: "Siparişleri Çek" düğmesi pazaryerinden GET ile çeker + idempotent upsert eder
/// (fiş/rezervasyon/stok YOK). Grid düzenleme/silme kapalı (read-only); sonuç raporu popup'ta gösterilir.
/// </summary>
public partial class OrderListPage : IDisposable
{
    [Inject] protected IOrderAppService OrderAppService { get; set; } = default!;
    [Inject] public ICrudStateService<OrderListDto, Guid> StateService { get; set; } = default!;
    [Inject] protected IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private IReadOnlyList<CrudToolbarAction>? _customActions;
    private GridListDataSource<OrderListDto>? _gridDataSource;

    private bool _showReport;
    private OrderFetchResultDto? _result;

    /// <summary>Server-side grid kaynağı — birleşik <see cref="IOrderAppService.GetListAsync"/>'e bağlı.</summary>
    public GridListDataSource<OrderListDto> GridDataSource
        => _gridDataSource ??= new GridListDataSource<OrderListDto>(FetchPageAsync)
        { OnError = ex => InvokeAsync(() => HandleErrorAsync(ex)) };

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
        // SALT-OKUMA panel: düzenleme/oluşturma/silme yok (rows tıklanabilir değil). Çekim ayrı custom action.
        StateService.IsGrantedCreate = StateService.IsGrantedUpdate = StateService.IsGrantedDelete = false;
        StateService.OnStateChanged += OnStateChangedHandler;

        _customActions = BuildCustomActions();
        return Task.CompletedTask;
    }

    // "Siparişleri Çek" — şirketin TÜM bağlı Trendyol kanallarından siparişleri çeker (tek düğme; kanal-başına dolaşma yok).
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
            await HandleErrorAsync(ex);
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

    // Çekim penceresi (şeffaflık): siparişler bu tarihten bugüne tarandı — pazaryerinin daralttığı aralık gizlenmez.
    private string ReportWindow => _result?.FetchedSinceUtc is { } since
        ? string.Format(L["Order:Fetch:Window"], since.ToString("yyyy-MM-dd"))
        : string.Empty;

    private string ReportIssues => _result is { } r ? string.Join(Environment.NewLine, r.Warnings) : string.Empty;

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

    private async Task HandleErrorAsync(Exception ex)
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
