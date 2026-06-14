using System;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Pages.Vaults;

public partial class VaultListPage
{
    public VaultListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IVaultAppService VaultAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        VaultGetDto, VaultListDto, Guid,
        VaultListRequestDto, VaultCreateDto, VaultUpdateDto> CrudAppService
        => VaultAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Vaults.Default;
}
