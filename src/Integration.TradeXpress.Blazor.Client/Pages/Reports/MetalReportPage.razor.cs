using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vaults;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Reports;

public partial class MetalReportPage
{
    [Inject] IWorkingContextService Working { get; set; } = default!;
    [Inject] IBranchAppService BranchAppService { get; set; } = default!;
    [Inject] IVaultAppService VaultAppService { get; set; } = default!;
    [Inject] IMetalAppService MetalAppService { get; set; } = default!;
    [Inject] IMetalReportAppService MetalReportAppService { get; set; } = default!;

    private List<BranchListDto>  _branches  = new();
    private List<VaultListDto>   _vaults    = new();
    private List<MetalListDto>   _metals    = new();
    private List<CommodityVariantOptionDto> _variants = new();

    private MetalReportFilterDto _filter = new()
    {
        Start = BusinessClock.Today().AddMonths(-1),
        End   = BusinessClock.Today(),
    };

    private List<MetalStockRowDto>?    _stockRows;
    private List<MetalMovementRowDto>? _movementRows;
    private int _tabIndex;

    protected override async Task OnInitializedAsync()
    {
        await Working.EnsureLoadedAsync();
        await LoadWorkingScopeAsync();   // şirket = working şirket (sunucuda zorlanır); şube listesi o şirkete ait
        _metals = await MetalAppService.GetPickerListAsync();
    }

    /// <summary>Kapsam = çalışılan (working) şirket — manuel şirket seçimi YOK (sızıntı önlemi). Şube listesi
    /// yalnız working şirketin şubeleri; şirket ayrıca sunucuda ICurrentCompany'den zorlanır.</summary>
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

    /// <summary>Maden seçilince varyant combo'sunu doldur; seçim değişince eski varyant filtresini sıfırla.</summary>
    private async Task OnMetalChanged(Guid? metalId)
    {
        _filter.MetalId   = metalId;
        _filter.VariantId = null;
        _variants.Clear();

        if (metalId is { } mid)
        {
            _variants = await MetalAppService.GetVariantPickerListAsync(mid);
        }
    }

    private async Task LoadAsync()
    {
        _stockRows    = await MetalReportAppService.GetStockAsync(_filter);
        _movementRows = await MetalReportAppService.GetMovementsAsync(_filter);
    }
}
