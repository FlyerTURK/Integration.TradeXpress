using System;
using System.Collections.Generic;
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

    /// <summary>Toolbar custom action — "İzinler" (SortIndex 300: Sil ile Arama arası).</summary>
    private IReadOnlyList<Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction> PermissionActions => new[]
    {
        new Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction
        {
            SortIndex = 300,
            Text = L["Permissions"],
            Tooltip = L["Permissions"],
            IconCssClass = TradeXpressIcons.Permission,
            Enabled = SelectedUser != null,
            OnClick = OpenPermissionsAsync,
        },
    };

    private async Task OpenPermissionsAsync()
    {
        var u = SelectedUser;
        if (u == null) return;
        var header = new Integration.Framework.Blazor.Client.Services.Mdi.TabHeaderData {
            FormCaption = L["Permissions"],
            IconCssClass = TradeXpressIcons.Permission,
            ParentLabel = L["User"],
            ParentValue = u.UserName
        };
        await TabManager.OpenOrActivateAsync($"/admin/permissions/U/{u.Id}", header);
    }

        // YENİ mimari: agnostic EntityEditForm + PersistentCoordinator + izin/rol-scope paneli bağları (eski UserEditPage kaldırıldı).
        public override System.Type EditComponentType => typeof(UserEditHost);
    }


