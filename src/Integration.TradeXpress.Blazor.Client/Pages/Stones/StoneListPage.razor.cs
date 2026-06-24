using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Stones;

namespace Integration.TradeXpress.Blazor.Client.Pages.Stones;

public partial class StoneListPage
{
    public StoneListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IStoneAppService StoneAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        StoneGetDto, StoneListDto, Guid,
        StoneListRequestDto, StoneCreateDto, StoneUpdateDto> CrudAppService
        => StoneAppService;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Stones.StoneEditHost);

    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<StoneListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:Stone:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }
}
