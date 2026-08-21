using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.TrendyolProducts;

/// <summary>
/// FİYATLANDIRMA TAHTASI kod-arkası — kanalın ürün listesi + satır eylemleri (Düzelt · Sil).
///
/// <para><b>PAZARYERİNE SIFIR YAZMA:</b> bu panel Trendyol'a HİÇBİR istek göndermez. Gösterilen pazaryeri
/// fiyatı/adedi import anının görüntüsüdür ve "Sil" yalnız YEREL kaydı kaldırır — Trendyol'daki listeleme
/// yerinde kalır. Yerel yazma tek noktadadır (<see cref="RemoveFromChannelAsync"/>).</para>
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

    [Inject]
    private IUiInteractionService Ui { get; set; } = null!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = null!;

    /// <summary>Kaydı YALNIZ bu kanaldan kaldırır — ürüne DOKUNMAZ.
    ///
    /// <para><b>Neden "Sil" değil "Kanaldan Kaldır":</b> aynı ürün N11'de de listelenmiş olabilir. Trendyol
    /// ekranındaki bir düğmenin ana ürünü silmesi, başka bir kanalı sessizce yıkmak olurdu. Ters yön zaten
    /// doğru kurulu (ürün silinince kanal kayıtları temizleniyor — <c>IProductChannelListingRemover</c>);
    /// bu yön ona simetrik olarak DAR tutuluyor.</para>
    ///
    /// <para><b>Trendyol'daki listeleme SİLİNMEZ:</b> app service'in kendi sözleşmesi de bunu söylüyor
    /// ("yalnız yerel siler; ürün Trendyol'da kalır"). Yani bu düğme "pazaryerinden kaldır" DEĞİL, "bizim
    /// yönetimimizden çıkar"dır — etiket bu yüzden dikkatle seçildi.</para></summary>
    private async Task RemoveFromChannelAsync(TrendyolPricingBoardItemDto item)
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

    /// <summary>Satırın ÜRÜNÜNÜ açar — kanal kaydını değil.
    ///
    /// <para><b>Neden ürün:</b> board'un bıraktığı iş (reçete + doğrulama) ÜRÜNE aittir. Ürün formu zaten
    /// hem ERP reçetesini hem de kanal ürünleri gridini taşıyor; kanal ayrıntısına oradan tek tıkla inilir.
    /// Doğrudan kanal formunu açsaydık kullanıcı reçeteye ulaşamazdı — ki board'un var oluş sebebi tam olarak
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
