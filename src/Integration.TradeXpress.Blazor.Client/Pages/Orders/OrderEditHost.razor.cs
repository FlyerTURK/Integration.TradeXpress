using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Orders;

/// <summary>Sipariş edit host — ince sarmal (coordinator kurar, geri kalan CrudEditHost'ta) + edit toolbar'ının
/// "Kabul Et"/"Reddet" özel aksiyonları (Sipariş Fazı O2 — TÜM bekleyen kalemleri TEK N11 isteğiyle işler).</summary>
public partial class OrderEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    private ICommitCoordinator<OrderDto, OrderListDto, Guid, OrderListRequestDto>? _coordinator;
    private OrderEditLayout? _layout;
    private bool _actionBusy;

    protected override void OnInitialized()
    {
        _coordinator = new OrderEditCoordinator(OrderAppService);
    }

    // Yalnız N11 kanalı VE bekleyen (Pending) kalem VE N11 sipariş durumu satıcı-aksiyonu BEKLİYORSA (AppService'te de
    // guard var; burada görünürlük) — sipariş yerelde tamamen işlenmişse YA DA N11 tarafında kapanmışsa (Tamamlandı/
    // İptal/Geçersiz — yerel Pending kalsa bile) butonlar hiç gösterilmez (tıklanacak anlamlı aksiyon kalmadı).
    // SortIndex 150/160: Delete(100) ile Previous(700) arası — liste toolbar'ının custom=300 slotuyla AYNI felsefe.
    private IReadOnlyList<CrudToolbarAction> BuildOrderActions(OrderDto model)
    {
        if (model.ChannelType != SalesChannelType.TrN11
            || model.PendingLineCount == 0
            || !N11OrderStatusCatalog.AwaitsSellerActionForOrder(model.RemoteStatus))
        {
            return Array.Empty<CrudToolbarAction>();
        }

        return new List<CrudToolbarAction>
        {
            new()
            {
                SortIndex = 150, Text = L["Order:Action:AcceptOrder"], Tooltip = L["Order:Action:AcceptOrder"],
                IconCssClass = TradeXpressIcons.CheckCircle + " xaf-toolbar-item-icon",
                Visible = true, Enabled = !_actionBusy, OnClick = () => AcceptOrderClickedAsync(model),
            },
            new()
            {
                SortIndex = 160, Text = L["Order:Action:RejectOrder"], Tooltip = L["Order:Action:RejectOrder"],
                IconCssClass = TradeXpressIcons.Close + " xaf-toolbar-item-icon",
                Visible = true, Enabled = !_actionBusy, OnClick = () => RejectOrderClickedAsync(model),
            },
        };
    }

    private async Task AcceptOrderClickedAsync(OrderDto model)
    {
        if (_actionBusy)
        {
            return;
        }

        // NumericSpinEdit alt sınır uygulamıyor (framework sarmalayıcısında MinValue yok) — N11'e GERÇEK/geri
        // alınamaz istek gitmeden önce geçersiz (≤0) paket sayısını burada engelle (OrderItemsDrill ile AYNI savunma).
        if (model.ActionInputNumberOfPackages < 1)
        {
            model.ActionInputNumberOfPackages = 1;
        }

        var confirmed = await UiService.ConfirmAsync(
            string.Format(L["Order:Action:ConfirmAcceptOrder"].Value, model.ActionInputNumberOfPackages),
            title: null, yesText: L["Yes"].Value, noText: L["Cancel"].Value, showCancel: false, defaultYes: false);
        if (confirmed != ConfirmDialogResult.Yes)
        {
            return;
        }

        await RunOrderActionAsync(model, () => OrderAppService.AcceptOrderAsync(new OrderAcceptDto
        {
            OrderId = model.Id,
            NumberOfPackages = model.ActionInputNumberOfPackages,
        }));
    }

    private async Task RejectOrderClickedAsync(OrderDto model)
    {
        if (_actionBusy)
        {
            return;
        }

        // Gerekçe önceden doldurulmuş bir alandan OKUNMAZ — kullanıcı burada GERÇEKTEN SORULUR (tek diyalog:
        // onay + serbest metin girişi). Boşken Evet devre dışı (PromptAsync inputRequired).
        var (confirmed, reason) = await UiService.PromptAsync(
            L["Order:Action:ConfirmRejectOrder"].Value,
            title: null, inputLabel: L["Order:Action:RejectReason"].Value,
            yesText: L["Order:Action:RejectOrder"].Value, noText: L["Cancel"].Value,
            showCancel: false, inputRequired: true);
        if (confirmed != ConfirmDialogResult.Yes || string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        await RunOrderActionAsync(model, () => OrderAppService.RejectOrderAsync(new OrderRejectDto
        {
            OrderId = model.Id,
            Reason = reason,
        }));
    }

    // Ortak koşturucu: N11'e yazar → kaç kalem etkilendiğini dostane bildirir + kalem drill'ini tazeler (drill
    // kendi verisini kendi yükler — parent bunu ReloadItemsAsync ile TETİKLER, veri kopyalamaz) + PendingLineCount'u
    // YERİNDE düşürür (sunucuya ikinci bir GetAsync gitmeden toolbar butonları anında güncel görünürlüğe geçer).
    private async Task RunOrderActionAsync(OrderDto model, Func<Task<OrderBulkActionResultDto>> action)
    {
        _actionBusy = true;
        StateHasChanged();
        try
        {
            var result = await action();
            if (result.AffectedCount == 0)
            {
                UiService.ShowWarningToast(L["Order:Action:NoPendingItems"].Value);
            }
            else
            {
                model.PendingLineCount = Math.Max(0, model.PendingLineCount - result.AffectedCount);
                UiService.ShowSuccessToast(L["SuccessfullySaved"].Value);
                if (_layout is not null)
                {
                    await _layout.ReloadItemsAsync();
                }
            }
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
        finally
        {
            _actionBusy = false;
            StateHasChanged();
        }
    }
}
