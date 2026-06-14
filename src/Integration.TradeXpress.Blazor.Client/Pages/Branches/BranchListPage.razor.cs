using System;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Pages.Branches;

public partial class BranchListPage
{
    public BranchListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IBranchAppService BranchAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        BranchGetDto, BranchListDto, Guid,
        BranchListRequestDto, BranchCreateDto, BranchUpdateDto> CrudAppService
        => BranchAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Branches.Default;
}
