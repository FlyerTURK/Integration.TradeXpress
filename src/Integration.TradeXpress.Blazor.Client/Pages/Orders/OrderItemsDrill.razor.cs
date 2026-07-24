using System;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Orders;

/// <summary>Sipariş kalemleri drill'i — kendi verisini <see cref="OrderId"/>'den yükler (IOrderAppService.
/// GetOrderLineEditsAsync), her satır kendi Kaydet'iyle SaveOrderLineEditAsync'e yazar (persistent DrillList).
/// Kabul/Red/Kargoya Ver — Sipariş Fazı O2 state machine aksiyonları, N11'e GERÇEKTEN yazar (geri alınamaz);
/// her biri önce onay diyaloğu ister.</summary>
public partial class OrderItemsDrill
{
    [Parameter, EditorRequired] public Guid OrderId { get; set; }

    /// <summary>Kalem durumu etiketi kanal-farkında (N11 kodu → etiket) için.</summary>
    [Parameter] public SalesChannelType ChannelType { get; set; }

    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private DrillList<OrderLineEditDto>? _drill;
    private Guid _loadedOrderId;
    private System.Collections.Generic.List<OrderLineEditDto>? _items;
    private bool _actionBusy;

    protected override async Task OnParametersSetAsync()
    {
        if (OrderId == _loadedOrderId && _items is not null)
        {
            return;
        }

        _loadedOrderId = OrderId;
        _items = await OrderAppService.GetOrderLineEditsAsync(OrderId);
    }

    /// <summary>Dışarıdan (OrderEditLayout — sipariş-düzeyi toplu Kabul Et/Reddet SONRASI) zorla tazeler.</summary>
    public async Task ReloadAsync()
    {
        _items = await OrderAppService.GetOrderLineEditsAsync(OrderId);
        StateHasChanged();
    }

    // AllowAdd=false olduğundan hiç tetiklenmez — DrillList'in EditorRequired sözleşmesi için gerekli.
    private OrderLineEditDto NewItem() => new() { OrderId = OrderId };

    private static OrderLineEditDto CloneItem(OrderLineEditDto source) => new()
    {
        RemoteLineId = source.RemoteLineId,
        OrderId = source.OrderId,
        ProductName = source.ProductName,
        ProductSellerCode = source.ProductSellerCode,
        Quantity = source.Quantity,
        Price = source.Price,
        Commission = source.Commission,
        DiscountTotal = source.DiscountTotal,
        Status = source.Status,
        Attributes = source.Attributes,
        ShipmentCompany = source.ShipmentCompany,
        TrackingNumber = source.TrackingNumber,
        CustomTexts = source.CustomTexts
            .Select(c => new OrderLineCustomTextEditDto { Option = c.Option, OriginalText = c.OriginalText, CorrectedText = c.CorrectedText })
            .ToList(),
        ProductVariantId = source.ProductVariantId,
        ProductSnapshotName = source.ProductSnapshotName,
        ProductSnapshotImageUrl = source.ProductSnapshotImageUrl,
        MatchedAt = source.MatchedAt,
        ActionStatus = source.ActionStatus,
        RejectReason = source.RejectReason,
        ActionAt = source.ActionAt,
        ActionInputNumberOfPackages = source.ActionInputNumberOfPackages,
        ActionInputRejectReason = source.ActionInputRejectReason,
        ActionInputShipmentCompanyId = source.ActionInputShipmentCompanyId,
        ActionInputTrackingNumber = source.ActionInputTrackingNumber,
        ActionInputCampaignNumber = source.ActionInputCampaignNumber,
        ActionInputShipmentMethod = source.ActionInputShipmentMethod,
    };

    private async Task<OrderLineEditDto> SaveLineAsync(OrderLineEditDto item)
    {
        var saved = await OrderAppService.SaveOrderLineEditAsync(item);
        ReplaceInList(saved);
        return saved;
    }

    private string? ItemStatusText(string? rawStatus)
        => ChannelType == SalesChannelType.TrN11 ? N11OrderStatusCatalog.ItemStatusLabel(rawStatus) : rawStatus;

    private static string Money(decimal? value) => value.HasValue ? value.Value.ToString("N2") : string.Empty;

    // N11 uzak durumu (item.Status) satıcı aksiyonu (Kabul/Red/Kargoya-Ver) bekliyor mu? N11 DIŞI kanalda gate yok
    // (mevcut davranış korunur — ileride kanala özel gate). N11'de yalnız {1,2,5} kodları aksiyon bekler.
    private bool AwaitsSellerAction(OrderLineEditDto item)
        => ChannelType != SalesChannelType.TrN11 || N11OrderStatusCatalog.AwaitsSellerAction(item.Status);

    private string ActionStatusLabel(OrderLineActionStatus status) => L[$"Order:Action:Status:{status}"];

    // ── Sipariş Fazı O2 — N11'e YAZAN aksiyonlar (GERÇEK, geri alınamaz). Her biri önce onay diyaloğu. ──

    private async Task AcceptClickedAsync(OrderLineEditDto item)
    {
        if (_actionBusy)
        {
            return;
        }

        // NumericSpinEdit alt sınır uygulamıyor (framework sarmalayıcısında MinValue yok) — N11'e GERÇEK/geri
        // alınamaz istek gitmeden önce geçersiz (≤0) paket sayısını burada engelle.
        if (item.ActionInputNumberOfPackages < 1)
        {
            item.ActionInputNumberOfPackages = 1;
        }

        var confirmed = await UiService.ConfirmAsync(
            string.Format(L["Order:Action:ConfirmAccept"].Value, item.ActionInputNumberOfPackages),
            title: null, yesText: L["Yes"].Value, noText: L["Cancel"].Value, showCancel: false, defaultYes: false);
        if (confirmed != ConfirmDialogResult.Yes)
        {
            return;
        }

        await RunActionAsync(item, () => OrderAppService.AcceptOrderLineAsync(new OrderLineAcceptDto
        {
            OrderId = item.OrderId,
            RemoteLineId = item.RemoteLineId,
            NumberOfPackages = item.ActionInputNumberOfPackages,
        }));
    }

    private async Task RejectClickedAsync(OrderLineEditDto item)
    {
        if (_actionBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.ActionInputRejectReason))
        {
            UiService.ShowWarningToast(L["Order:Action:RejectReasonRequired"].Value);
            return;
        }

        var confirmed = await UiService.ConfirmAsync(
            L["Order:Action:ConfirmReject"].Value,
            title: null, yesText: L["Yes"].Value, noText: L["Cancel"].Value, showCancel: false, defaultYes: false);
        if (confirmed != ConfirmDialogResult.Yes)
        {
            return;
        }

        await RunActionAsync(item, () => OrderAppService.RejectOrderLineAsync(new OrderLineRejectDto
        {
            OrderId = item.OrderId,
            RemoteLineId = item.RemoteLineId,
            Reason = item.ActionInputRejectReason!,
        }));
    }

    private async Task ShipClickedAsync(OrderLineEditDto item)
    {
        if (_actionBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.ActionInputShipmentCompanyId) || string.IsNullOrWhiteSpace(item.ActionInputTrackingNumber))
        {
            UiService.ShowWarningToast(L["Order:Action:ShipmentFieldsRequired"].Value);
            return;
        }

        var confirmed = await UiService.ConfirmAsync(
            L["Order:Action:ConfirmShip"].Value,
            title: null, yesText: L["Yes"].Value, noText: L["Cancel"].Value, showCancel: false, defaultYes: false);
        if (confirmed != ConfirmDialogResult.Yes)
        {
            return;
        }

        await RunActionAsync(item, () => OrderAppService.ShipOrderLineAsync(new OrderLineShipDto
        {
            OrderId = item.OrderId,
            RemoteLineId = item.RemoteLineId,
            ShipmentCompanyId = item.ActionInputShipmentCompanyId!,
            TrackingNumber = item.ActionInputTrackingNumber!,
            CampaignNumber = item.ActionInputCampaignNumber,
            ShipmentMethod = item.ActionInputShipmentMethod,
        }));
    }

    // Ortak aksiyon çalıştırıcı: N11'e yazar → başarılıysa AÇIK popup'taki item'ı YERİNDE günceller (aynı referans,
    // anında görünür) + arka plan listesini (_items) senkronlar. Hata → dostane toast, popup açık kalır.
    private async Task RunActionAsync(OrderLineEditDto item, Func<Task<OrderLineEditDto>> action)
    {
        _actionBusy = true;
        try
        {
            var updated = await action();
            CopyServerFields(updated, item);
            ReplaceInList(updated);
            UiService.ShowSuccessToast(L["SuccessfullySaved"].Value);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
        finally
        {
            _actionBusy = false;
        }
    }

    // Sunucudan dönen taze durumu AÇIK popup'ın item'ına kopyalar (aksiyon girdi alanları hariç — operatör az önce
    // yazdıklarını kaybetmesin).
    private static void CopyServerFields(OrderLineEditDto from, OrderLineEditDto to)
    {
        to.ActionStatus = from.ActionStatus;
        to.RejectReason = from.RejectReason;
        to.ActionAt = from.ActionAt;
    }

    private void ReplaceInList(OrderLineEditDto updated)
    {
        var idx = _items!.FindIndex(x => x.RemoteLineId == updated.RemoteLineId);
        if (idx >= 0)
        {
            _items[idx] = updated;
        }
    }
}
