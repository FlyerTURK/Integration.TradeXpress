using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.TrendyolCategories;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.TrendyolCategories;

/// <summary>Trendyol kategori senkronizasyon paneli — kanal edit formunun içinde (yalnız kaydedilmiş kanalda) açılır.
/// Buton, host-global kategori ağacını Trendyol'dan çekip yerel DB'ye upsert eder (pazaryerine SIFIR yazma) ve kaç
/// kategori senkronlandığını toast'lar. N11 kargo şablonu "İçe Aktar" deseniyle simetrik (self-contained + best-effort UI).</summary>
public partial class TrendyolCategorySyncPanel : CrudComponentBase
{
    [Inject] private ITrendyolCategoryAppService CategoryAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // Çift-tıklama/eşzamanlı sync engeli — istek dönene kadar buton devre dışı.
    private bool _syncing;

    // Senkronize et: Trendyol'dan ağacı çek → yerelde upsert; senkronlanan sayısını toast'la, hatayı dostane göster.
    private async Task SyncAsync()
    {
        _syncing = true;
        try
        {
            var count = await CategoryAppService.SyncCategoriesAsync();
            UiService.ShowSuccessToast(L["Trendyol:Category:SyncSuccess", count]);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["Trendyol:Category:SyncFailed"].Value);
        }
        finally
        {
            _syncing = false;
        }
    }
}
