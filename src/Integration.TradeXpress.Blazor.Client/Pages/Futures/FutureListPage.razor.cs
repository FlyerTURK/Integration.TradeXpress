using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Futures;

namespace Integration.TradeXpress.Blazor.Client.Pages.Futures;

public partial class FutureListPage
{
    public FutureListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IFutureAppService FutureAppService { get; set; } = default!;

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
        FutureGetDto, FutureListDto, Guid,
        FutureListRequestDto, FutureCreateDto, FutureUpdateDto> CrudAppService
        => FutureAppService;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Futures.FutureEditHost);

    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<FutureListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:Future:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }

    /// <summary>Toolbar'ın özel aksiyonu: "Ürün Oluştur". Her çizimde kurulur çünkü etkinliği SEÇİME
    /// bağlıdır.</summary>
    private IReadOnlyList<CrudToolbarAction> BuildCustomActions()
    {
        var selected = StateService.SelectedDataItems?.OfType<FutureListDto>().ToList() ?? new List<FutureListDto>();

        return new List<CrudToolbarAction>
        {
            CommodityProductAction.Build(
                L,
                selected.Count,
                () => OpenProductFromFutureAsync(selected.Count == 1 ? selected[0].Id : Guid.Empty)),
        };
    }

    /// <summary>Vadelinin ürün projeksiyonunu ÜRÜN formunda açar (kayıt AÇILMAZ — seed). "Vadeli varyant
    /// barındırmaz" (2026-08-08): ürün tek ana varyantla doğar, kodu vadelinin kodudur.</summary>
    private async Task OpenProductFromFutureAsync(Guid futureId)
    {
        if (futureId == Guid.Empty)
        {
            return;
        }

        try
        {
            var seed = await FutureAppService.ProjectToProductAsync(futureId);

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
