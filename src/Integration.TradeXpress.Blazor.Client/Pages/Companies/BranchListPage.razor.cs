using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client.Pages.Companies.Models;
using Integration.TradeXpress.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Companies;

public partial class BranchListPage
    : CrudPageBase<BranchGetDto, BranchListDto, Guid, BranchListRequestDto, BranchCreateDto, BranchUpdateDto, BranchTreeItemViewModel>
{
    public BranchListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Parameter]
    public Guid CompanyId { get; set; }

    [SupplyParameterFromQuery(Name = "companycode")]
    public string? CompanyCode { get; set; }

    [Inject] protected IBranchAppService BranchAppService { get; set; } = default!;
    [Inject] protected ITabManager TabManager { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        BranchGetDto, BranchListDto, Guid,
        BranchListRequestDto, BranchCreateDto, BranchUpdateDto> CrudAppService
        => BranchAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Branches.Default;

    private string PageTitle => string.IsNullOrWhiteSpace(CompanyCode)
        ? L["Menu:Branches"]
        : $"{L["Menu:Branches"]} - [{L["Entity:Company"]}: {CompanyCode}]";

    protected override void OnConfiguringListRequest(BranchListRequestDto request)
        => request.CompanyId = CompanyId;

    // ── Toolbar drill: Şube → Kasalar ───────────────────────────────────────────
    private BranchListDto? SelectedBranch =>
        StateService.SelectedDataItems is { Count: 1 } sel ? sel[0] as BranchListDto : null;

    // Drill-down artık URL navigasyonu değil — kasaları MDI sekmesi olarak açar/aktive eder.
    private async Task OpenVaultsAsync()
    {
        if (SelectedBranch is null) return;
        var url = $"/vaults/{SelectedBranch.Id}?branchcode={Uri.EscapeDataString(SelectedBranch.Code)}";
        var title = $"{L["Menu:Vaults"]} - [{L["Entity:Branch"]}: {SelectedBranch.Code}]";
        await TabManager.OpenOrActivateAsync(url, title, TradeXpressIcons.Vault);
    }

    public override Task BeforeCreateAsync()
    {
        StateService.EditingModel = new BranchTreeItemViewModel { IsActive = true };
        StateService.ShowEditPage(isNewRecord: true);
        return Task.CompletedTask;
    }

    public override async Task BeforeUpdateAsync(BranchListDto entity)
    {
        StateService.SetDataRowSelected(entity);
        await ExecuteAsync(async () =>
        {
            var dto = await BranchAppService.GetAsync(entity.Id);
            StateService.EditingModel = ToBranchVm(dto);
            StateService.ShowEditPage(isNewRecord: false);
        });
    }

    public override async Task SaveAsync()
    {
        var model = StateService.EditingModel!;
        await ExecuteAsync(async () =>
        {
            if (StateService.IsNewRecord)
            {
                await BranchAppService.CreateAsync(new BranchCreateDto
                {
                    CompanyId = CompanyId,
                    Code = model.Code,
                    Name = model.Name,
                    IsHeadquarters = model.IsHeadquarters,
                    DisplayOrder = model.DisplayOrder,
                    Description = model.Description,
                });
            }
            else
            {
                await BranchAppService.UpdateAsync(model.Id!.Value, new BranchUpdateDto
                {
                    Code = model.Code,
                    Name = model.Name,
                    IsHeadquarters = model.IsHeadquarters,
                    IsActive = model.IsActive,
                    DisplayOrder = model.DisplayOrder,
                    Description = model.Description,
                });
            }
            StateService.HideEditPage();
            StateService.RequestReload();
            await Notify.Success(L["SuccessfullySaved"]);
        });
    }

    private static BranchTreeItemViewModel ToBranchVm(BranchGetDto d) => new()
    {
        Id = d.Id,
        Code = d.Code,
        Name = d.Name,
        IsHeadquarters = d.IsHeadquarters,
        IsActive = d.IsActive,
        DisplayOrder = d.DisplayOrder,
        Description = d.Description,
    };
}
