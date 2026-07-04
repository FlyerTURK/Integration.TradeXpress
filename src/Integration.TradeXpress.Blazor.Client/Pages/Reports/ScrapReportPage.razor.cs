using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Vaults;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Reports;

public partial class ScrapReportPage
{
    [Inject] IWorkingContextService Working { get; set; } = default!;
    [Inject] IBranchAppService BranchAppService { get; set; } = default!;
    [Inject] IVaultAppService VaultAppService { get; set; } = default!;
    [Inject] IScrapAppService ScrapAppService { get; set; } = default!;
    [Inject] IScrapReportAppService ScrapReportAppService { get; set; } = default!;

    private List<BranchListDto> _branches = new();
    private List<VaultListDto> _vaults = new();
    private List<ScrapListDto> _scraps = new();

    private ScrapReportFilterDto _filter = new()
    {
        Start = BusinessClock.Today().AddMonths(-1),
        End   = BusinessClock.Today(),
    };

    private List<ScrapStockRowDto>?    _stockRows;
    private List<ScrapMovementRowDto>? _movementRows;
    private int _tabIndex;

    protected override async Task OnInitializedAsync()
    {
        await Working.EnsureLoadedAsync();
        await LoadWorkingScopeAsync();
        _scraps = await ScrapAppService.GetPickerListAsync();
    }

    /// <summary>Kapsam = çalışılan (working) şirket (sızıntı önlemi); şube listesi yalnız o şirketin şubeleri.</summary>
    private async Task LoadWorkingScopeAsync()
    {
        _filter.CompanyId = Working.CurrentCompanyId;
        _filter.BranchId  = null;
        _filter.VaultId   = null;
        _branches.Clear();
        _vaults.Clear();

        if (Working.CurrentCompanyId is { } cid)
        {
            var branches = await BranchAppService.GetListAsync(new BranchListRequestDto { MaxResultCount = 200 });
            var all = branches.Items as List<BranchListDto> ?? new(branches.Items);
            _branches = all.Where(b => b.CompanyId == cid).ToList();
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
