using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Volo.Abp.Application.Services;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Pages.Financials.Parities;

public partial class ParityListPage
{
    public ParityListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Inject]
    protected IParityAppService ParityAppService { get; set; } = default!;

    public override ICrudAppService<
        ParityGetDto, ParityListDto, Guid,
        ParityListRequestDto, ParityCreateDto, ParityUpdateDto> CrudAppService => ParityAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Parities.Default;

    // Edit'i popup yerine MDI sekmesinde aç (route'lu ParityEditPage + IMdiTabOpener üzerinden).
    protected override EditOpenTarget EditOpenTarget => EditOpenTarget.MdiTab;

    public override Type EditComponentType => typeof(ParityEditPage);

    // Tenant, global (host) pariteyi silemez — server zaten reddeder; bu erken/anlaşılır UX bloğu.
    public override async Task DeleteAsync()
    {
        var selected = StateService.SelectedDataItems;
        if (selected == null || selected.Count == 0)
            return;

        if (CurrentTenant.Id != null && selected.OfType<ParityListDto>().Any(x => x.IsGlobal))
        {
            UiService.ShowWarningToast(L["TradeXpress:Parity:CannotDeleteGlobalAsTenant"]);
            return;
        }

        await base.DeleteAsync();
    }
}
