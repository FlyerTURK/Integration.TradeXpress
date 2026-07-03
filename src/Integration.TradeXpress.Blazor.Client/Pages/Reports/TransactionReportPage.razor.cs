using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Reports;

/// <summary>
/// İşlem Raporu — cari-hesap-BAĞIMSIZ, Company/Branch/Vault scoped, tarih aralıklı voucher satırları.
/// Şirket sunucuda ICurrentCompany'den zorlanır; şube default'u working context'ten gelir.
/// Sayfalama server-side (DxPager + PagedResultDto); Excel export tüm satırları çekip grid'den aktarır.
/// </summary>
public partial class TransactionReportPage
{
    private const int PageSize = 100;

    [Inject] IWorkingContextService Working { get; set; } = default!;
    [Inject] IBranchAppService BranchAppService { get; set; } = default!;
    [Inject] IVaultAppService VaultAppService { get; set; } = default!;
    [Inject] ITransactionReportAppService TransactionReportAppService { get; set; } = default!;
    [Inject] IGridExportAssemblyLoader ExportLoader { get; set; } = default!;

    private List<BranchListDto> _branches = new();
    private List<VaultListDto> _vaults = new();

    private readonly TransactionReportRequestDto _request = new();
    private DateTime _start = DateTime.Today.AddMonths(-1);
    private DateTime _end = DateTime.Today;
    private ProcessType? _selectedType;

    private List<TransactionReportRowDto>? _rows;
    private long _totalCount;
    private int _pageIndex;
    private bool _busy;
    private TxGrid? _grid;

    /// <summary>Tip filtresi combo seçenekleri (tr görünen adlar — mevcut rapor sayfalarıyla aynı dil yaklaşımı).</summary>
    private sealed record TypeOption(ProcessType Value, string Name);

    private static readonly List<TypeOption> _typeOptions = new()
    {
        new(ProcessType.Metal,    "Maden"),
        new(ProcessType.Scrap,    "Hurda"),
        new(ProcessType.Cash,     "Nakit"),
        new(ProcessType.Convert,  "Çevrim"),
        new(ProcessType.Service,  "Hizmet"),
        new(ProcessType.Future,   "Vadeli"),
        new(ProcessType.Stone,    "Taş"),
        new(ProcessType.Jewelry,  "Mücevher"),
        new(ProcessType.Transfer, "Virman"),
        new(ProcessType.Assay,    "Çeşni"),
        new(ProcessType.Bullion,  "Takoz"),
    };

    private int PageCount
    {
        get { return (int)Math.Max(1, (_totalCount + PageSize - 1) / PageSize); }
    }

    protected override async Task OnInitializedAsync()
    {
        await Working.EnsureLoadedAsync();
        await LoadWorkingScopeAsync();
    }

    /// <summary>Kapsam = çalışılan (working) şirket — manuel şirket seçimi YOK (sızıntı önlemi; şirket
    /// ayrıca sunucuda ICurrentCompany'den zorlanır). Şube default'u = working şube.</summary>
    private async Task LoadWorkingScopeAsync()
    {
        _branches.Clear();
        _vaults.Clear();

        if (Working.CurrentCompanyId is { } cid)
        {
            var branches = await BranchAppService.GetListAsync(new BranchListRequestDto { MaxResultCount = 200 });
            _branches = branches.Items.Where(b => b.CompanyId == cid).ToList();
        }

        // Working şube default'u (varsa) — kullanıcı "(Tümü)" için temizleyebilir.
        if (Working.CurrentBranchId is { } bid && _branches.Any(b => b.Id == bid))
        {
            await OnBranchChanged(bid);
        }
    }

    private async Task OnBranchChanged(Guid? branchId)
    {
        _request.BranchId = branchId;
        _request.VaultId = null;
        _vaults.Clear();

        if (branchId != null)
        {
            var vaults = await VaultAppService.GetListAsync(new VaultListRequestDto { MaxResultCount = 200 });
            _vaults = vaults.Items.Where(v => v.BranchId == branchId).ToList();
        }
    }

    private async Task LoadPageAsync(int pageIndex)
    {
        _busy = true;
        try
        {
            _pageIndex = pageIndex;
            var result = await TransactionReportAppService.GetListAsync(BuildRequest(
                skipCount: pageIndex * PageSize, maxResultCount: PageSize));
            _totalCount = result.TotalCount;
            _rows = result.Items.ToList();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Excel export — server-side sayfalı grid'de tüm satırları (üst sınırlı) çekip grid'den aktarır,
    /// sonra görünen sayfayı geri yükler.</summary>
    private async Task ExportToExcelAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var all = await TransactionReportAppService.GetListAsync(BuildRequest(
                skipCount: 0, maxResultCount: 10_000));
            _rows = all.Items.ToList();
            StateHasChanged();
            await Task.Delay(100);   // grid'in tüm-satır verisini render etmesini bekle (export grid verisinden okur)

            await ExportLoader.EnsureLoadedAsync();
            await (_grid?.InnerGrid).ExportToXlsxSafeAsync("TransactionReport");
        }
        finally
        {
            _busy = false;
        }

        await LoadPageAsync(_pageIndex);   // görünen sayfayı geri yükle
    }

    private TransactionReportRequestDto BuildRequest(int skipCount, int maxResultCount)
    {
        return new TransactionReportRequestDto
        {
            BranchId       = _request.BranchId,
            VaultId        = _request.VaultId,
            SubAccountId   = _request.SubAccountId,
            Start          = _start.Date,
            EndExclusive   = _end.Date.AddDays(1),
            Types          = _selectedType is { } t ? new List<ProcessType> { t } : null,
            SkipCount      = skipCount,
            MaxResultCount = maxResultCount,
        };
    }
}
