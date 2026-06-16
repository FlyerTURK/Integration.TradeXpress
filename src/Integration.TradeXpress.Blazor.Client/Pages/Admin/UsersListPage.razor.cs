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

public partial class UsersListPage
{
    public UsersListPage()
    {
        LocalizationResource = typeof(TradeXpressResource);
    }

    [Inject] protected UserCrudAdapter UserAdapter { get; set; } = default!;
    [Inject] protected ITabManager TabManager { get; set; } = default!;

    public override ICrudAppService<UserGetDto, UserListDto, Guid, UserListRequestDto, CreateIdentityUserInput, UpdateIdentityUserInput> CrudAppService
        => UserAdapter;

    protected override string PermissionPrefix => "AbpIdentity.Users";

    protected override string EntityChangeKey => IdentityViewKeys.Users;

    private UserListDto? SelectedUser => StateService.SelectedDataItems?.OfType<UserListDto>().FirstOrDefault();

    private async Task OpenPermissionsAsync()
    {
        var u = SelectedUser;
        if (u == null) return;
        await TabManager.OpenOrActivateAsync($"/admin/permissions/U/{u.Id}", $"{L["Permissions"]}: {u.UserName}", TradeXpressIcons.Permission);
    }
}
