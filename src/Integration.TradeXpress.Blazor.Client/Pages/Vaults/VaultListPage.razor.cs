using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Pages.Vaults.Models;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Vaults;

public partial class VaultListPage
{
    public VaultListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Parameter]
    public Guid BranchId { get; set; }

    [SupplyParameterFromQuery(Name = "branchcode")]
    public string? BranchCode { get; set; }

    [Inject]
    protected IVaultAppService VaultAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        VaultGetDto, VaultListDto, Guid,
        VaultListRequestDto, VaultCreateDto, VaultUpdateDto> CrudAppService
        => VaultAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Vaults.Default;

    private string PageTitle => string.IsNullOrWhiteSpace(BranchCode)
        ? L["Menu:Vaults"]
        : $"{L["Menu:Vaults"]} - [{L["Entity:Branch"]}: {BranchCode}]";

    // Drill-down: yalnız bu şubeye ait kasalar.
    protected override void OnConfiguringListRequest(VaultListRequestDto request)
        => request.BranchId = BranchId;

    // Yeni kasa: parent şube route'tan gelir (combo'da pre-set).
    public override Task BeforeCreateAsync()
    {
        StateService.EditingModel = new VaultViewModel { BranchId = BranchId, IsActive = true };
        StateService.ShowEditPage(isNewRecord: true);
        return Task.CompletedTask;
    }
}
