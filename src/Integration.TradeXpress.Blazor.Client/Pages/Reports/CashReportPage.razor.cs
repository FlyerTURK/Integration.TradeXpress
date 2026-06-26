using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Vaults;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Reports;

public partial class CashReportPage
{
    [Inject] IWorkingContextService Working { get; set; } = default!;
    [Inject] IBranchAppService BranchAppService { get; set; } = default!;
    [Inject] IVaultAppService VaultAppService { get; set; } = default!;
    [Inject] ICashAppService CashAppService { get; set; } = default!;
    [Inject] ICashReportAppService CashReportAppService { get; set; } = default!;

    private List<BranchListDto> _branches = new();
    private List<VaultListDto> _vaults = new();
    private List<CashListDto> _cashes = new();

    private CashReportFilterDto _filter = new()
    {
        Start = DateTime.Today.AddMonths(-1),
        End = DateTime.Today,
    };

    private List<CashStockRowDto>? _stockRows;
    private List<CashMovementRowDto>? _movementRows;
    private int _tabIndex;

    protected override async Task OnInitializedAsync()
    {
        await Working.EnsureLoadedAsync();
        await LoadWorkingScopeAsync();
        _cashes = await CashAppService.GetPickerListAsync();
        if (_cashes.Count > 0) _filter.CashId = _cashes[0].Id;
    }

    /// <summary>Kapsam = çalışılan (working) şirket (sızıntı önlemi); şube listesi yalnız o şirketin şubeleri.</summary>
    private async Task LoadWorkingScopeAsync()
    {
        _filter.CompanyId = Working.CurrentCompanyId;
        _filter.BranchId = null;
        _filter.VaultId = null;
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
        _filter.VaultId = null;
        _vaults.Clear();

        if (branchId != null)
        {
            var vaults = await VaultAppService.GetListAsync(new VaultListRequestDto { MaxResultCount = 200 });
            _vaults = vaults.Items as List<VaultListDto> ?? new(vaults.Items);
        }
    }

    private async Task LoadAsync()
    {
        _stockRows = await CashReportAppService.GetStockAsync(_filter);
        _movementRows = await CashReportAppService.GetMovementsAsync(_filter);
    }
}
