using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Jewelries;

namespace Integration.TradeXpress.Blazor.Client.Pages.Jewelries;

public partial class JewelryListPage
{
    public JewelryListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IJewelryAppService JewelryAppService { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected Integration.TradeXpress.Blazor.Client.Services.Working.IWorkingContextService Working { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected IViewOpener ViewOpener { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await Working.EnsureLoadedAsync();
        await base.OnInitializedAsync();
    }

    protected override void OnConfiguringListRequest(JewelryListRequestDto request)
    {
        request.CompanyId = Working.CurrentCompanyId;
    }

    public override Volo.Abp.Application.Services.ICrudAppService<
        JewelryGetDto, JewelryListDto, Guid,
        JewelryListRequestDto, JewelryCreateDto, JewelryUpdateDto> CrudAppService
        => JewelryAppService;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Jewelries.JewelryEditHost);

    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<JewelryListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:Jewelry:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }

    /// <summary>Toolbar'ın özel aksiyonu: "Ürün Oluştur". Her çizimde kurulur çünkü etkinliği SEÇİME
    /// bağlıdır.</summary>
    private IReadOnlyList<CrudToolbarAction> BuildCustomActions()
    {
        var selected = StateService.SelectedDataItems?.OfType<JewelryListDto>().ToList() ?? new List<JewelryListDto>();

        return new List<CrudToolbarAction>
        {
            CommodityProductAction.Build(
                L,
                selected.Count,
                () => OpenProductFromJewelryAsync(selected.Count == 1 ? selected[0].Id : Guid.Empty)),
        };
    }

    /// <summary>Mücevherin ürün projeksiyonunu ÜRÜN formunda açar (kayıt AÇILMAZ — seed). Mücevher ÇEKİRDEK
    /// varyantlı ailedir: nitelik + varyant grafı ve medya taşınır, fiyat taşınmaz (mücevherde fiyat
    /// entity seviyesindedir; üründe reçeteden türetilir).</summary>
    private async Task OpenProductFromJewelryAsync(Guid jewelryId)
    {
        if (jewelryId == Guid.Empty)
        {
            return;
        }

        try
        {
            var seed = await JewelryAppService.ProjectToProductAsync(jewelryId);

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
