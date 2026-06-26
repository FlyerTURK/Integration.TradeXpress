using System;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Reports;

/// <summary>
/// Pozisyon raporu — kapsam DAİMA çalışılan (working) şube; manuel şirket/şube seçimi YOK (bilgi sızıntısı
/// önlenir). Şirket sunucuda ICurrentCompany'den zorlanır; client yalnız working şube Id'sini taşır.
/// Sol menüdeki working seçici değişince (<see cref="IWorkingContextService.Changed"/>) rapor yenilenir.
/// 5sn'de bir, yalnız sayfa açıkken. DURUM = base-dışı değerlenmiş net açık.
/// </summary>
public partial class PositionReportPage
{
    [Inject] IWorkingContextService Working { get; set; } = default!;
    [Inject] IPositionReportAppService PositionReportAppService { get; set; } = default!;

    private PositionReportFilterDto _filter = new();
    private PositionReportResultDto? _result;
    private PeriodicTimer? _timer;

    /// <summary>true = şirket geneli (working şirketin TÜM şubeleri toplanır); false = yalnız working şube.</summary>
    private bool _companyMode;

    protected override async Task OnInitializedAsync()
    {
        await Working.EnsureLoadedAsync();
        Working.Changed += OnWorkingChanged;
        BuildFilter();
        await LoadAsync();
        _ = AutoRefreshLoopAsync();   // 5sn poll — yalnız sayfa açıkken
    }

    /// <summary>Kapsam = company-mode'da TÜM working şirket (BranchId=null → sunucu working şirketle sınırlar);
    /// aksi halde yalnız working şube. Şirket client'tan GÖNDERİLMEZ — sunucu ICurrentCompany'den zorlar.</summary>
    private void BuildFilter()
        => _filter = new PositionReportFilterDto { BranchId = _companyMode ? null : Working.CurrentBranchId };

    private async Task OnCompanyModeChanged(bool value)
    {
        _companyMode = value;
        BuildFilter();
        await LoadAsync();
        StateHasChanged();
    }

    private void OnWorkingChanged()
        => _ = InvokeAsync(async () => { BuildFilter(); await LoadAsync(); StateHasChanged(); });

    private string WorkingLabel()
    {
        var b = Working.CurrentBranch;
        return b is null ? "—" : $"{b.CompanyDisplay} / {b.BranchDisplay}";
    }

    private async Task LoadAsync() => _result = await PositionReportAppService.GetAsync(_filter);

    private async Task AutoRefreshLoopAsync()
    {
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await _timer.WaitForNextTickAsync())
            {
                await LoadAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { /* sayfa kapandı */ }
    }

    /// <summary>Net işaretine göre renk: long(+) yeşil, short(−) kırmızı, 0 nötr.</summary>
    private static string SignColor(decimal v)
        => v > 0 ? "var(--dxbl-success, #2e7d32)" : v < 0 ? "var(--dxbl-danger, #c62828)" : "inherit";

    /// <summary>DURUM rengi: açık(−) kırmızı, aksi yeşil.</summary>
    private static string DurumColor(decimal v)
        => v < 0 ? "var(--dxbl-danger, #c62828)" : "var(--dxbl-success, #2e7d32)";

    public void Dispose()
    {
        Working.Changed -= OnWorkingChanged;
        _timer?.Dispose();
    }
}
