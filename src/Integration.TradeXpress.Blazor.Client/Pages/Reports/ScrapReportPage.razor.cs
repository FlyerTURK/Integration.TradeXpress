using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Vaults;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Reports;

public partial class ScrapReportPage
{
    [Inject] ICompanyAppService CompanyAppService { get; set; } = default!;
    [Inject] IBranchAppService BranchAppService { get; set; } = default!;
    [Inject] IVaultAppService VaultAppService { get; set; } = default!;
    [Inject] IScrapAppService ScrapAppService { get; set; } = default!;
    [Inject] IScrapReportAppService ScrapReportAppService { get; set; } = default!;

    private List<CompanyListDto> _companies = new();
    private List<BranchListDto> _branches = new();
    private List<VaultListDto> _vaults = new();
    private List<ScrapListDto> _scraps = new();

    private ScrapReportFilterDto _filter = new()
    {
        Start = DateTime.Today.AddMonths(-1),
        End   = DateTime.Today,
    };

    private List<ScrapStockRowDto>?    _stockRows;
    private List<ScrapMovementRowDto>? _movementRows;
    private int _tabIndex;

    protected override async Task OnInitializedAsync()
    {
        var companies = await CompanyAppService.GetListAsync(new CompanyListRequestDto { MaxResultCount = 200 });
        _companies = companies.Items as List<CompanyListDto> ?? new(companies.Items);
        _scraps = await ScrapAppService.GetPickerListAsync();
    }

    private async Task OnCompanyChanged(Guid? companyId)
    {
        _filter.CompanyId = companyId;
        _filter.BranchId  = null;
        _filter.VaultId   = null;
        _branches.Clear();
        _vaults.Clear();

        if (companyId != null)
        {
            var branches = await BranchAppService.GetListAsync(new BranchListRequestDto { MaxResultCount = 200 });
            _branches = branches.Items as List<BranchListDto> ?? new(branches.Items);
        }
    }

    private async Task OnBranchChanged(Guid? branchId)
    {
        _filter.BranchId = branchId;
        _filter.VaultId  = null;
        _vaults.Clear();

        if (branchId != null)
        {
            var vaults = await VaultAppService.GetListAsync(new VaultListRequestDto { MaxResultCount = 200 });
            _vaults = vaults.Items as List<VaultListDto> ?? new(vaults.Items);
        }
    }

    private async Task LoadAsync()
    {
        _stockRows    = await ScrapReportAppService.GetStockAsync(_filter);
        _movementRows = await ScrapReportAppService.GetMovementsAsync(_filter);
    }
}
