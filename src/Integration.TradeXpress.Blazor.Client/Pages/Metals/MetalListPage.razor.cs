using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Metals;

namespace Integration.TradeXpress.Blazor.Client.Pages.Metals;

public partial class MetalListPage
{
    public MetalListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IMetalAppService MetalAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        MetalGetDto, MetalListDto, Guid,
        MetalListRequestDto, MetalCreateDto, MetalUpdateDto> CrudAppService
        => MetalAppService;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Metals.MetalEditHost);

    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<MetalListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:Metal:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }
}
