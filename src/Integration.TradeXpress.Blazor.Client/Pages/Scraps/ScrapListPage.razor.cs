using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Scraps;

namespace Integration.TradeXpress.Blazor.Client.Pages.Scraps;

public partial class ScrapListPage
{
    public ScrapListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IScrapAppService ScrapAppService { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected Integration.TradeXpress.Blazor.Client.Services.Mdi.ITabManager TabManager { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected IViewOpener ViewOpener { get; set; } = default!;

    private async Task OpenUnitAsync(Guid? unitId, string? code)
    {
        if (unitId is not { } id || id == Guid.Empty)
        {
            return;
        }

        await TabManager.OpenOrActivateAsync(
            $"/currencies/currency-units/{id}",
            $"{L["CurrencyUnit"]}: {code}",
            TradeXpressIcons.CurrencyUnit);
    }

    public override Volo.Abp.Application.Services.ICrudAppService<
        ScrapGetDto, ScrapListDto, Guid,
        ScrapListRequestDto, ScrapCreateDto, ScrapUpdateDto> CrudAppService
        => ScrapAppService;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Scraps.ScrapEditHost);

    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<ScrapListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:Scrap:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }

    /// <summary>Toolbar'ın özel aksiyonları: hurda raporu + "Ürün Oluştur". Her çizimde kurulur çünkü
    /// "Ürün Oluştur"un etkinliği SEÇİME bağlıdır (bkz. <c>MetalListPage</c>'teki aynı gerekçe).</summary>
    private IReadOnlyList<CrudToolbarAction> BuildCustomActions()
    {
        var selected = StateService.SelectedDataItems?.OfType<ScrapListDto>().ToList() ?? new List<ScrapListDto>();

        return new List<CrudToolbarAction>
        {
            new CrudToolbarAction
            {
                SortIndex = 150,
                Text = L["ScrapReport"].Value,
                AdaptiveText = L["ScrapReport"].Value,
                Tooltip = L["ScrapReport"].Value,
                IconCssClass = "custom-icon-report",
                OnClick = async () => await TabManager.OpenOrActivateAsync("/reports/scrap", L["ScrapReport"].Value, "custom-icon-report"),
            },
            CommodityProductAction.Build(
                L,
                selected.Count,
                () => OpenProductFromScrapAsync(selected.Count == 1 ? selected[0].Id : Guid.Empty)),
        };
    }

    /// <summary>Hurdanın ürün projeksiyonunu ÜRÜN formunda açar (kayıt AÇILMAZ — seed). Hurda VARYANTSIZ bir
    /// ailedir; ürün tek ana varyantla doğar ve o varyantın kodu hurdanın kodudur.</summary>
    private async Task OpenProductFromScrapAsync(Guid scrapId)
    {
        if (scrapId == Guid.Empty)
        {
            return;
        }

        try
        {
            var seed = await ScrapAppService.ProjectToProductAsync(scrapId);

            await ViewOpener.OpenAsync(
                typeof(ProductEditHost),
                null,
                L["Product"].Value,
                iconCssClass: null,
                extraParams: new Dictionary<string, object> { ["SeedModel"] = seed });
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex);
        }
    }
}
