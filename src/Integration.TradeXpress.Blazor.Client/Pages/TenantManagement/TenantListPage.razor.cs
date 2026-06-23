using Integration.TradeXpress.Tenants;

namespace Integration.TradeXpress.Blazor.Client.Pages.TenantManagement;

public partial class TenantListPage
{
    public TenantListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected ITenantAppService TenantAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<TenantGetDto, TenantListDto, System.Guid, TenantListRequestDto, TenantCreateDto, TenantUpdateDto> CrudAppService => TenantAppService;

    protected override string PermissionPrefix => Volo.Abp.TenantManagement.TenantManagementPermissions.Tenants.Default;

        // YENİ mimari: agnostic EntityEditForm + PersistentCoordinator (eski TenantEditPage kaldırıldı).
        public override System.Type EditComponentType => typeof(Integration.TradeXpress.Blazor.Client.Pages.TenantManagement.TenantEditHost);
    }


