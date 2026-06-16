using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Pages.Admin.Models;
using Integration.TradeXpress.Blazor.Client.Services;
using Integration.TradeXpress.Blazor.Client.Services.Identity;
using Integration.TradeXpress.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Localization;
using Microsoft.AspNetCore.Components;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Blazor.Client.Pages.Admin;

public partial class RolesListPage
{
    public RolesListPage()
    {
        LocalizationResource = typeof(TradeXpressResource);
    }

    [Inject] protected RoleCrudAdapter RoleAdapter { get; set; } = default!;
    [Inject] protected ITabManager TabManager { get; set; } = default!;

    public override ICrudAppService<RoleGetDto, RoleListDto, Guid, RoleListRequestDto, CreateIdentityRoleInput, UpdateIdentityRoleInput> CrudAppService
        => RoleAdapter;

    protected override string PermissionPrefix => "AbpIdentity.Roles";

    protected override string EntityChangeKey => IdentityViewKeys.Roles;

    private RoleListDto? SelectedRole => StateService.SelectedDataItems?.OfType<RoleListDto>().FirstOrDefault();

    private async Task OpenPermissionsAsync()
    {
        var r = SelectedRole;
        if (r == null) return;
        await TabManager.OpenOrActivateAsync($"/admin/permissions/R/{Uri.EscapeDataString(r.Name)}", $"{L["Permissions"]}: {r.Name}", TradeXpressIcons.Permission);
    }
}
