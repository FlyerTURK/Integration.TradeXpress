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
///
/// <para><b>"Karar Bekleyenler" sekmesi</b> (2026-08-21 Hakan yerleşim kararı): Blocked rezervasyonlar + iptal
/// talepleri + yaşlanan rezervler tek listede (<see cref="IOrderAppService.GetPendingDecisionsAsync"/>).
/// Sekme başlığı bekleyen sayısını taşır; satırdan sipariş AYNI yolla (OrderEditHost) açılır, karar oradaki
/// rezervasyon panelinde verilir.</para>
/// </summary>
public partial class OrderListPage : IDisposable
{
    /// <summary>"Karar Bekleyenler" sekmesinin indeksi — markup'taki sekme SIRASIYLA eşleşmek zorunda
    /// (araya sekme girerse güncellenir; ProductLayout'taki *TabIndex sabiti deseni).</summary>
    private const int PendingDecisionsTabIndex = 1;

    [Inject] protected IOrderAppService OrderAppService { get; set; } = default!;
    [Inject] public ICrudStateService<OrderListDto, Guid> StateService { get; set; } = default!;
    [Inject] protected IUiInteractionService UiService { get; set; } = default!;
    [Inject] protected IViewOpener ViewOpener { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private IReadOnlyList<CrudToolbarAction>? _customActions;
    private GridListDataSource<OrderListDto>? _gridDataSource;

    private int _activeTabIndex;
    private List<OrderPendingDecisionDto> _pendingRows = new();

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

    protected override async Task OnInitializedAsync()
    {
        // Create/Delete YOK (Order yalnız senkronizasyondan gelir); DÜZENLEME var (Sipariş Fazı O1). Çekim ayrı custom action.
        StateService.IsGrantedCreate = StateService.IsGrantedDelete = false;
        StateService.IsGrantedUpdate = true;
        StateService.OnStateChanged += OnStateChangedHandler;

        _customActions = BuildCustomActions();

        // Rozet sayısı AÇILIŞTA yüklenir — sekmeye girilmeden de görünmeli (rozetin amacı davet etmek;
        // sekmeye girince yüklenseydi bekleyen iş ancak arayan kullanıcıya görünürdü).
        await ReloadPendingAsync();
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

            // Çekim zinciri rezervasyon üretir (Blocked/iptal talebi dahil) → karar rozeti de tazelenir.
            await ReloadPendingAsync();
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

    private async Task ReloadAsync()
    {
        StateService.RequestReload();

        // Kayıt sonrası karar listesi de tazelenir: verilen karar (onay/red/serbest bırakma/elle eşleme)
        // satırı sekmeden düşürmüş olabilir — bayat rozetle karar verilmesin.
        await ReloadPendingAsync();
    }

    // ── "Karar Bekleyenler" sekmesi ───────────────────────────────────────────────────────────────────

    /// <summary>Sekme başlığı — bekleyen sayısı rozet yerine başlığın içinde ("Karar Bekleyenler (3)").
    /// Basit sayı yeterli; yeni rozet bileşeni icat edilmedi (2026-08-21 dilim kararı).</summary>
    private string PendingTabCaption
    {
        get
        {
            var title = L["Order:PendingDecisions"].Value;
            return _pendingRows.Count > 0 ? $"{title} ({_pendingRows.Count})" : title;
        }
    }

    /// <summary>Sekme değişimi — karar sekmesine HER girişte liste tazelenir (arka plandaki senkron worker'ı
    /// yeni Blocked/iptal kaydı yazmış olabilir; OnDemand render içerik durumunu koruduğundan kendiliğinden
    /// yenilenmez).</summary>
    private async Task OnActiveTabChangedAsync(int index)
    {
        _activeTabIndex = index;
        if (index == PendingDecisionsTabIndex)
        {
            await ReloadPendingAsync();
        }
    }

    /// <summary>Karar bekleyenler listesini tazeler — sekme başlığındaki sayı da bundan çıkar.</summary>
    private async Task ReloadPendingAsync()
    {
        try
        {
            _pendingRows = await OrderAppService.GetPendingDecisionsAsync();
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex);
        }
    }

    /// <summary>Karar bekleyen satırdan siparişi açar — sipariş listesindekiyle AYNI yol (OrderEditHost popup);
    /// karar oradaki rezervasyon panelinde verilir. Kayıtta iki liste birden tazelenir (ReloadAsync).</summary>
    private Task OpenPendingAsync(OrderPendingDecisionDto row)
    {
        var extra = new Dictionary<string, object>
        {
            { "OnSaved", EventCallback.Factory.Create(this, ReloadAsync) },
        };
        return ViewOpener.OpenAsync(typeof(OrderEditHost), row.OrderId, string.Empty, TradeXpressIcons.SalesChannel, extra);
    }

    /// <summary>Tip rozeti — ConfirmationGrid rozet deseni (inline stil: Bootstrap bu app'te etkisiz, yeni CSS
    /// sınıfı onay ister). Renk aciliyeti söyler: iptal talebi AMBER (insan kararı bekliyor) · kurulamadı
    /// KIRMIZI (sistem kuramadı, sipariş stok tutmuyor) · yaşlanan MAVİ (bilgi — süre aşımı yok, yalnız görünürlük).</summary>
    private static string KindBadgeStyle(OrderPendingDecisionKind kind)
    {
        var background = kind switch
        {
            OrderPendingDecisionKind.CancellationRequested => "#f59e0b",
            OrderPendingDecisionKind.BlockedReservation    => "#dc2626",
            _                                              => "#3b82f6",
        };
        return $"display:inline-block; padding:2px 8px; border-radius:10px; font-size:12px; font-weight:600; color:#fff; background:{background};";
    }

    /// <summary>Yaş kolonu — "ne kadardır bekliyor" (gün/saat/dk). SÜRE olduğundan timezone çevirisi gerekmez
    /// (UTC çıpa − UTC şimdi); tarih kolonlarındaki UtcLocalText burada bilerek kullanılmadı.</summary>
    private string FormatAge(DateTime sinceUtc)
    {
        var age = DateTime.UtcNow - sinceUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;   // saat kayması koruması — negatif yaş basılmaz
        }

        if (age.TotalDays >= 1)
        {
            return string.Format(L["Order:PendingDecision:AgeDays"], (int)age.TotalDays);
        }

        if (age.TotalHours >= 1)
        {
            return string.Format(L["Order:PendingDecision:AgeHours"], (int)age.TotalHours);
        }

        return string.Format(L["Order:PendingDecision:AgeMinutes"], (int)age.TotalMinutes);
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
