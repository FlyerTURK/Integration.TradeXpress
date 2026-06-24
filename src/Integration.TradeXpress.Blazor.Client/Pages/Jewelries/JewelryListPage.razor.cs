using System;
using System.Linq;
using System.Threading.Tasks;
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
}
