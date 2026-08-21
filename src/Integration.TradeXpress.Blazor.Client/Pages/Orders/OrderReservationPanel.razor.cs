using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Orders;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Orders;

/// <summary>
/// Rezervasyon panelinin davranışı — kendi verisini <see cref="OrderId"/>'den yükler (<c>OrderItemsDrill</c>
/// deseni: parent veri kopyalamaz, panel kendi servisini çağırır).
/// </summary>
public partial class OrderReservationPanel
{
    [Parameter, EditorRequired] public Guid OrderId { get; set; }

    [Inject] protected IOrderAppService OrderAppService { get; set; } = default!;
    [Inject] protected IUiInteractionService UiService { get; set; } = default!;
    [Inject] protected Integration.TradeXpress.Blazor.Client.Services.Working.IWorkingContextService Working { get; set; } = default!;

    private OrderReservationDto? _reservation;
    private bool _loading = true;
    private bool _busy;
    private Guid _loadedOrderId;

    private bool IsDecisionPending =>
        _reservation is { CancellationDecision: OrderCancellationDecision.Pending };

    private bool IsReserved =>
        _reservation is { Status: OrderReservationStatus.Reserved };

    /// <summary>Stok ekseninin rengi: rezerve YEŞİL değil MAVİdir — "hazır" değil "taahhüt edildi" demektir.
    /// Bloklanmış KIRMIZI: kullanıcının müdahalesi olmadan sipariş karşılanamaz.</summary>
    private string StatusCssClass => _reservation?.Status switch
    {
        OrderReservationStatus.Reserved  => "text-primary fw-bold",
        OrderReservationStatus.Fulfilled => "text-success fw-bold",
        OrderReservationStatus.Blocked   => "text-danger fw-bold",
        _ => "text-muted fw-bold",
    };

    /// <summary>Karar ekseni: BEKLİYOR turuncu — kullanıcıdan iş isteyen tek durum odur.</summary>
    private string DecisionCssClass => _reservation?.CancellationDecision switch
    {
        OrderCancellationDecision.Pending  => "text-warning fw-bold",
        OrderCancellationDecision.Approved => "text-danger fw-bold",
        OrderCancellationDecision.Rejected => "text-success fw-bold",
        _ => "text-muted",
    };

    /// <summary><b>null ≠ 0</b>: "beyan edilmedi" ile "fark yok" farklı bilgilerdir. Boş hücre göstermek
    /// ikisini birbirine karıştırırdı — fiyat farkı ASLA türetilmez, yalnız kullanıcı girer.</summary>
    private string PriceDifferenceText(OrderFulfillmentLinkDto link)
    {
        return link.PriceDifference is { } value
            ? value.ToString("N2")
            : L["Order:Reservation:PriceDifferenceNotDeclared"].Value;
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        // Aynı sipariş için tekrar yükleme yapma (OnParametersSet her render'da koşar).
        if (OrderId == Guid.Empty || OrderId == _loadedOrderId)
        {
            return;
        }

        _loadedOrderId = OrderId;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _loading = true;
        try
        {
            _reservation = await OrderAppService.GetReservationAsync(OrderId);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>İptal kararı. <b>Onay stoğu GERİ VERİR</b> — bu yüzden not sorulur ve onay diyaloğu geçilir;
    /// tek tıkla geri alınamaz bir işlem olmamalı.</summary>
    private async Task DecideAsync(bool approve)
    {
        if (_busy)
        {
            return;
        }

        var (confirmed, note) = await UiService.PromptAsync(
            approve
                ? L["Order:Reservation:ConfirmApprove"].Value
                : L["Order:Reservation:ConfirmReject"].Value,
            title: null,
            inputLabel: L["Order:Reservation:DecisionNote"].Value,
            yesText: L["Yes"].Value, noText: L["Cancel"].Value,
            showCancel: false, inputRequired: false);

        if (confirmed != ConfirmDialogResult.Yes)
        {
            return;
        }

        await RunAsync(() => OrderAppService.DecideCancellationAsync(new OrderCancellationDecisionDto
        {
            OrderId = OrderId,
            Approve = approve,
            Note = note,
        }));
    }

    private async Task ReleaseAsync()
    {
        if (_busy)
        {
            return;
        }

        var (confirmed, reason) = await UiService.PromptAsync(
            L["Order:Reservation:ConfirmRelease"].Value,
            title: null,
            inputLabel: L["Order:Reservation:ReleaseReason"].Value,
            yesText: L["Order:Reservation:Release"].Value, noText: L["Cancel"].Value,
            showCancel: false, inputRequired: true);

        if (confirmed != ConfirmDialogResult.Yes || string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        await RunAsync(() => OrderAppService.ReleaseReservationAsync(new OrderReservationReleaseDto
        {
            OrderId = OrderId,
            Reason = reason,
        }));
    }

    /// <summary>FİZİKİ ÇIKIŞ — rezervasyonu gerçek çıkışa çevirir.
    ///
    /// <para><b>Kasa çalışma bağlamından alınır</b> (kullanıcı ayrıca seçmez): malı hazırlayan, o an hangi
    /// kasada çalışıyorsa odur. Ayrı bir seçici koymak, yanlış kasadan çıkış yapmayı kolaylaştırırdı.</para>
    ///
    /// <para>Onay metni sonucu AÇIKÇA söyler — bu işlemden sonra iptal reddedilir ve rezervasyon serbest
    /// bırakılamaz. Fiyat farkı beyanı bu dilimde girilmez (satır-başı beyan ekranı ayrı iş); beyan
    /// edilmediğinde <c>null</c> kalır, yani "fark yok" DEMEZ.</para></summary>
    private async Task FulfillAsync()
    {
        if (_busy)
        {
            return;
        }

        if (Working.CurrentBranchId is not { } branchId || Working.CurrentVaultId is not { } vaultId)
        {
            UiService.ShowWarningToast(L["Order:Reservation:FulfillNeedsVault"].Value);
            return;
        }

        var (confirmed, note) = await UiService.PromptAsync(
            L["Order:Reservation:ConfirmFulfill"].Value,
            title: null,
            inputLabel: L["Order:Reservation:DecisionNote"].Value,
            yesText: L["Order:Reservation:Fulfill"].Value, noText: L["Cancel"].Value,
            showCancel: false, inputRequired: false);

        if (confirmed != ConfirmDialogResult.Yes)
        {
            return;
        }

        await RunAsync(() => OrderAppService.FulfillReservationAsync(new OrderFulfillmentInputDto
        {
            OrderId = OrderId,
            BranchId = branchId,
            VaultId = vaultId,
            Note = note,
        }));
    }

    /// <summary>Ortak koşturucu: sunucudan DÖNEN kaydı doğrudan bağlar — ikinci bir GET atmadan panel güncel
    /// olur ve "kaydettim ama ekran eski" hâli oluşmaz.</summary>
    private async Task RunAsync(Func<Task<OrderReservationDto>> action)
    {
        _busy = true;
        try
        {
            _reservation = await action();
            UiService.ShowSuccessToast(L["SavedSuccessfully"].Value);
        }
        finally
        {
            _busy = false;
        }
    }
}
