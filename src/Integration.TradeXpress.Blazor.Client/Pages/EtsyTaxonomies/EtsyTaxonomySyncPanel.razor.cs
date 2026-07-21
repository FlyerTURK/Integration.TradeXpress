using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.EtsyTaxonomies;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.EtsyTaxonomies;

/// <summary>Etsy taksonomi senkronizasyon paneli — kanal edit formunun içinde (yalnız kaydedilmiş kanalda) açılır.
/// Buton, host-global taksonomi ağacını Etsy'den çekip yerel DB'ye upsert eder (pazaryerine SIFIR yazma) ve kaç
/// düğüm senkronlandığını toast'lar. TrendyolCategorySyncPanel ikizi (self-contained + best-effort UI).</summary>
public partial class EtsyTaxonomySyncPanel : CrudComponentBase
{
    [Inject] private IEtsyTaxonomyAppService TaxonomyAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // Çift-tıklama/eşzamanlı sync engeli — istek dönene kadar buton devre dışı.
    private bool _syncing;

    // Senkronize et: Etsy'den ağacı çek → yerelde upsert; senkronlanan sayısını toast'la, hatayı dostane göster.
    private async Task SyncAsync()
    {
        _syncing = true;
        try
        {
            var count = await TaxonomyAppService.SyncTaxonomyAsync();
            UiService.ShowSuccessToast(L["Etsy:Taxonomy:SyncSuccess", count]);
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["Etsy:Taxonomy:SyncFailed"].Value);
        }
        finally
        {
            _syncing = false;
        }
    }
}
