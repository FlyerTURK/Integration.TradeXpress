using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Goods;

namespace Integration.TradeXpress.Blazor.Client.Pages.Goods;

public partial class GoodListPage
{
    public GoodListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IGoodAppService GoodAppService { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected Integration.TradeXpress.Blazor.Client.Services.Working.IWorkingContextService Working { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await Working.EnsureLoadedAsync();
        await base.OnInitializedAsync();
    }

    protected override void OnConfiguringListRequest(GoodListRequestDto request)
    {
        request.CompanyId = Working.CurrentCompanyId;
    }

    public override Volo.Abp.Application.Services.ICrudAppService<
        GoodGetDto, GoodListDto, Guid,
        GoodListRequestDto, GoodCreateDto, GoodUpdateDto> CrudAppService
        => GoodAppService;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Goods.GoodEditHost);

    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<GoodListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:Good:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }
}
