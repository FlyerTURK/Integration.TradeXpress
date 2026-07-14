using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vaults;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Reports;

public partial class GoodStockReportPage
{
    [Inject] IWorkingContextService Working { get; set; } = default!;
    [Inject] IBranchAppService BranchAppService { get; set; } = default!;
    [Inject] IVaultAppService VaultAppService { get; set; } = default!;
    [Inject] IGoodAppService GoodAppService { get; set; } = default!;
    [Inject] IGoodReportAppService GoodReportAppService { get; set; } = default!;

    private List<BranchListDto> _branches = new();
    private List<VaultListDto>  _vaults   = new();
    private List<GoodListDto>   _goods    = new();
    private List<CommodityVariantOptionDto> _variants = new();

    private GoodReportFilterDto _filter = new()
    {
        Start = BusinessClock.Today().AddMonths(-1),
        End   = BusinessClock.Today(),
    };

    private List<GoodStockRowDto>? _stockRows;

    protected override async Task OnInitializedAsync()
    {
        await Working.EnsureLoadedAsync();
        await LoadWorkingScopeAsync();   // şirket = working şirket (sunucuda ICurrentCompany'den zorlanır)
        _goods = await GoodAppService.GetPickerListAsync(Working.CurrentCompanyId);
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

    /// <summary>Mamül seçilince varyant combo'sunu doldur; seçim değişince eski varyant filtresini sıfırla.</summary>
    private async Task OnGoodChanged(Guid? goodId)
    {
        _filter.GoodId    = goodId;
        _filter.VariantId = null;
        _variants.Clear();

        if (goodId is { } gid)
        {
            _variants = await GoodAppService.GetVariantPickerListAsync(gid);
        }
    }

    private async Task LoadAsync()
    {
        _stockRows = await GoodReportAppService.GetStockAsync(_filter);
    }
}
