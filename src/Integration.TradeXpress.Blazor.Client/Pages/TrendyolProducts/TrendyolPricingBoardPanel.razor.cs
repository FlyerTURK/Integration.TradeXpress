using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.TrendyolProducts;

/// <summary>
/// FİYATLANDIRMA TAHTASI kod-arkası. Salt okuma: hiçbir yazma yolu YOK (pazaryerine de, yerele de).
///
/// <para><b>Neden kendi paneli:</b> içe aktarım tek tıkla bitiyor ama arkasından 103 ürünlük elle iş bırakıyor
/// (canlı ölçüm: 104 üründen 103'ü reçetesiz, 144 varyantın 144'ü Draft). Fiyat kararı ancak pazaryerindeki
/// mevcut fiyat/adet ile yan yana görülünce verilebiliyordu; bu panel o kıyası tek ekrana indiriyor.</para>
///
/// <para><b>Otomatik yüklenmez:</b> panel açılır açılmaz sorgu koşturmak, kullanıcının bakmadığı sekmede
/// gereksiz iş demek (arka plan sekmelerinin boşa hesaplatması zaten kayıtlı bir sorun). Kullanıcı isteyince
/// yüklenir.</para>
/// </summary>
public partial class TrendyolPricingBoardPanel
{
    private List<TrendyolPricingBoardItemDto> _items = new();
    private bool _busy;
    private bool _loaded;

    /// <summary>Tahtası gösterilecek Trendyol satış kanalı.</summary>
    [Parameter]
    public Guid SalesChannelId { get; set; }

    [Inject]
    private ISalesChannelTrTrendyolProductAppService AppService { get; set; } = null!;

    [Inject]
    private IViewOpener ViewOpener { get; set; } = null!;

    [Inject]
    private IPopupService PopupService { get; set; } = null!;

    /// <summary>Satırın ÜRÜNÜNÜ açar — kanal kaydını değil.
    ///
    /// <para><b>Neden ürün:</b> tahtanın bıraktığı iş (reçete + doğrulama) ÜRÜNE aittir. Ürün formu zaten
    /// hem ERP reçetesini hem de kanal ürünleri gridini taşıyor; kanal ayrıntısına oradan tek tıkla inilir.
    /// Doğrudan kanal formunu açsaydık kullanıcı reçeteye ulaşamazdı — ki tahtanın var oluş sebebi tam olarak
    /// o eksiği kapatmak.</para>
    ///
    /// <para>Merkezî yol (<see cref="IViewOpener"/>) kullanılır — ham <c>DxPopup</c> YASAK (liste sayfalarının
    /// New/Edit akışıyla aynı mekanizma).</para></summary>
    private Task OpenProductAsync(TrendyolPricingBoardItemDto item)
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
            // Hata durumunda da meşguliyet KALKAR — aksi hâlde panel kalıcı olarak kilitli görünürdü
            // ve kullanıcı yeniden denemek için sayfayı yenilemek zorunda kalırdı.
            _busy = false;
        }
    }
}
