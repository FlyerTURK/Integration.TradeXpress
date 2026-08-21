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

    /// <summary>Toolbar custom action — "İzinler" (SortIndex 300: Sil ile Arama arası).</summary>
    private IReadOnlyList<Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction> PermissionActions => new[]
    {
        new Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction
        {
            SortIndex = 300,
            Text = L["Permissions"],
            Tooltip = L["Permissions"],
            IconCssClass = TradeXpressIcons.Permission,
            Enabled = SelectedRole != null,
            OnClick = OpenPermissionsAsync,
        },
    };

    private async Task OpenPermissionsAsync()
    {
        var r = SelectedRole;
        if (r == null) return;
        var header = new Integration.Framework.Blazor.Client.Services.Mdi.TabHeaderData {
            FormCaption = L["Permissions"],
            IconCssClass = TradeXpressIcons.Permission,
            ParentLabel = L["Role"],
            ParentValue = r.Name
        };
        await TabManager.OpenOrActivateAsync($"/admin/permissions/R/{Uri.EscapeDataString(r.Name)}", header);
    }

        // YENİ mimari: agnostic EntityEditForm + PersistentCoordinator + izin paneli bağları (eski RoleEditPage kaldırıldı).
        public override System.Type EditComponentType => typeof(RoleEditHost);
    }


