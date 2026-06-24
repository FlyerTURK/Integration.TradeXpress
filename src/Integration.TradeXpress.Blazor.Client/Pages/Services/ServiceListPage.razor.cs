using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Services;

namespace Integration.TradeXpress.Blazor.Client.Pages.Services;

public partial class ServiceListPage
{
    public ServiceListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IServiceAppService ServiceAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        ServiceGetDto, ServiceListDto, Guid,
        ServiceListRequestDto, ServiceCreateDto, ServiceUpdateDto> CrudAppService
        => ServiceAppService;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Services.ServiceEditHost);

    // Tenant, global (host) Service'i silemez — UI tarafında da engelle (server zaten bloklar).
    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<ServiceListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:Service:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }
}
