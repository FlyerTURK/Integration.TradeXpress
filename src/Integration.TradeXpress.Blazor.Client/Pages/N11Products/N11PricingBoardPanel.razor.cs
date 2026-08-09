using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.N11Products;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Products;

/// <summary>
/// N11 ÇALIŞMA TAHTASI kod-arkası — Trendyol tahtasının ikizi.
///
/// <para><b>PAZARYERİNE SIFIR YAZMA:</b> panel N11'e hiçbir istek göndermez. Yerel yazma tek noktadadır
/// (<see cref="RemoveFromChannelAsync"/>) ve o da yalnız bizim kaydımızı kaldırır.</para>
///
/// <para><b>Neden Trendyol paneliyle ortak bir bileşen değil:</b> kolonlar ayrışıyor (N11 pazaryeri
/// fiyatı/adedi taşımıyor, satış/onay durumu taşıyor) ve DTO'lar farklı. Ortaklaştırılan şey SUNUCU
/// tarafındaki karar sinyali oldu (<c>ChannelProductBoardBuilder</c>) — asıl sapma riski oradaydı.
/// İki ince razor'u tek generic bileşene zorlamak, kazancından çok okunaklılık kaybettirirdi.</para>
/// </summary>
public partial class N11PricingBoardPanel
{
    private List<N11PricingBoardItemDto> _items = new();
    private bool _busy;
    private bool _loaded;

    /// <summary>Tahtası gösterilecek N11 satış kanalı.</summary>
    [Parameter]
    public Guid SalesChannelId { get; set; }

    [Inject]
    private ISalesChannelTrN11ProductAppService AppService { get; set; } = null!;

    [Inject]
    private IViewOpener ViewOpener { get; set; } = null!;

    [Inject]
    private IPopupService PopupService { get; set; } = null!;

    [Inject]
    private IUiInteractionService Ui { get; set; } = null!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = null!;

    /// <summary>Kaydı YALNIZ bu kanaldan kaldırır — ürüne DOKUNMAZ, N11'deki listelemeyi de SİLMEZ.
    /// Gerekçe Trendyol panelindekiyle aynı: ürün başka kanalda da listelenmiş olabilir.</summary>
    private async Task RemoveFromChannelAsync(N11PricingBoardItemDto item)
    {
        if (await Ui.ConfirmDeleteAsync(L["TrendyolProduct:PricingBoard:RemoveConfirm", item.ProductCode].Value)
            != ConfirmDialogResult.Yes)
        {
            return;
        }

        try
        {
            await AppService.DeleteAsync(item.Id);
            Ui.ShowSuccessToast(L["TrendyolProduct:PricingBoard:Removed", item.ProductCode].Value);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Ui.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
        }
    }

    /// <summary>Satırın ÜRÜNÜNÜ açar — kanal kaydını değil. Reçete ve doğrulama ürüne aittir; ürün formu
    /// zaten kanal kayıtları gridini de taşıdığı için kanal ayrıntısına oradan inilir.</summary>
    private Task OpenProductAsync(N11PricingBoardItemDto item)
    {
        if (item.ProductId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        var extra = new Dictionary<string, object>
        {
            { "OnClosed", EventCallback.Factory.Create(this, () => PopupService.Close()) },
        };

        return ViewOpener.OpenAsync(typeof(Products.ProductEditHost), item.ProductId, string.Empty, null, extra);
    }

    private async Task LoadAsync()
    {
        if (_busy || SalesChannelId == Guid.Empty)
        {
            return;
        }

        _busy = true;
        try
        {
            _items = await AppService.GetPricingBoardAsync(SalesChannelId);
            _loaded = true;
        }
        finally
        {
            // Hata durumunda da meşguliyet KALKAR — panel kalıcı kilitli görünmesin.
            _busy = false;
        }
    }
}
