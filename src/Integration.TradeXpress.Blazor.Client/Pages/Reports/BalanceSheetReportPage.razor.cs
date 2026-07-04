using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.Blazor.Client.Services.Mdi;
using Integration.Framework.Blazor.Client.Services.Base;
using DevExpress.Blazor;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Reports;

/// <summary>
/// Bilanço raporu — FULL net-varlık (pozisyon = exposure alt-kümesi). Akış: kapsam(Şube/Şirket) switch + tarih →
/// "Bilanço Al" (canlı hesapla, kaydetmez) → önizle → "Kaydet" (snapshot'a yaz). Kapsam DAİMA çalışılan şirket/şube
/// (sızıntı önlemi, pozisyonla aynı; şirket sunucuda ICurrentCompany'den zorlanır). Manuel — auto-refresh YOK.
/// </summary>
public partial class BalanceSheetReportPage
{
    [Inject] IWorkingContextService Working { get; set; } = default!;
    [Inject] IBalanceSheetReportAppService BalanceSheetReportAppService { get; set; } = default!;
    [Inject] ITabManager TabManager { get; set; } = default!;

    private BalanceSheetReportResultDto? _result;

    /// <summary>true = şirket konsolide (working şirketin tüm şubeleri); false = yalnız working şube.</summary>
    private bool _companyMode;
    private DateTime _asOf = DateTime.Today;
    private bool _busy;
    private bool _isMobile;   // DxLayoutBreakpoint (≤768px) — mobilde split dikey istiflenir.
    private object? _selectedDetailRow;   // üst detay grid seçili satırı (BalanceSheetDetailRowDto).

    protected override async Task OnInitializedAsync()
    {
        await Working.EnsureLoadedAsync();
        Working.Changed += OnWorkingChanged;
        UpdateTabTitle();
        await ComputeAsync();   // form açılışında grid otomatik dolsun (varsayılan kapsam Şube + bugün).
        await LoadSnapshotsAsync();   // kaydedilen bilanço geçmişini (sağ grid) da yükle.
    }

    /// <summary>Tab başlığını workspace KODUYLA güncelle (sadece kodlar yeterli): "Bilanço · PALAZZO / INTERNET". Tab yoksa no-op.</summary>
    private void UpdateTabTitle()
    {
        if (TabManager.ActiveTabId is { } id && Working.CurrentBranch is { } b)
            TabManager.SetTabTitle(id, $"Bilanço · {b.CompanyBranchCode}");
    }

    /// <summary>Filtre: company-mode → BranchId null (sunucu working şirketle sınırlar); aksi halde working şube.
    /// Şirket client'tan GÖNDERİLMEZ — sunucu ICurrentCompany'den zorlar.</summary>
    private BalanceSheetReportFilterDto BuildFilter()
    {
        return new()
        {
            Scope    = _companyMode ? BalanceSheetScope.Company : BalanceSheetScope.Branch,
            BranchId = _companyMode ? null : Working.CurrentBranchId,
            AsOf     = _asOf,
        };
    }

    private async Task ComputeAsync()
    {
        _busy = true;
        try { _result = await BalanceSheetReportAppService.ComputeAsync(BuildFilter()); }
        finally { _busy = false; }
    }

    /// <summary>Şirket/Şube switch değişince kapsamı uygula + grid'i OTOMATİK yeniden hesapla (reload) + geçmişi tazele.</summary>
    private async Task OnCompanyModeChanged(bool value)
    {
        _companyMode = value;
        await ComputeAsync();
        await LoadSnapshotsAsync();
    }

    // ── Drill popup: üst grid çift-tık → o kategori×birim değerinin HAREKETLERİ (Kod | Bakiye) ──
    private bool _movementsVisible;
    private bool _movementsBusy;
    private BalanceSheetMovementResultDto? _movements;
    private BalanceSheetDetailRowDto? _movementRow;

    /// <summary>Üst detay grid çift-tık → seçili satırın hareketlerini sunucudan çekip popup'ta gösterir. Grup satırı / seçim yoksa no-op.</summary>
    private async Task OnRowDoubleClick(GridRowClickEventArgs args)
    {
        if (_selectedDetailRow is not BalanceSheetDetailRowDto row || _result is null) return;
        _movementRow      = row;
        _movements        = null;
        _movementsBusy    = true;
        _movementsVisible = true;
        StateHasChanged();
        try
        {
            _movements = await BalanceSheetReportAppService.GetMovementsAsync(new BalanceSheetMovementRequestDto
            {
                Category = row.Category,
                UnitId   = row.UnitId,
                Scope    = _companyMode ? BalanceSheetScope.Company : BalanceSheetScope.Branch,
                BranchId = _companyMode ? null : Working.CurrentBranchId,
                AsOf     = _result.AsOf,
            });
        }
        finally { _movementsBusy = false; }
    }

    private async Task SaveAsync()
    {
        if (_result is null) return;
        _busy = true;
        try
        {
            _result = await BalanceSheetReportAppService.SaveAsync(BuildFilter());
            await LoadSnapshotsAsync();   // yeni snapshot geçmiş grid'inde görünsün.
            // Başarı toast'ı: uygulamanın STANDART yolu = IUiInteractionService → DevExpress IToastNotificationService
            // → MainLayout'taki <DxToastProvider> render eder. (ABP Notify.Success bu app'te no-op: abp.notify stub.)
            Ui.ShowSuccessToast(Loc["BalanceSheet:Saved"]);
        }
        finally { _busy = false; }
    }

    /// <summary>DÖNEM SIFIRLA: kapsam-farkındalıklı ONAY → onaylanırsa P&L dönem başlangıcını AsOf'a ilerlet + snapshot
    /// dondur (atomik, sunucuda) → toast + geçmiş grid tazele. Resmi GL devir/prim YAPILMAZ (onay metninde belirtilir).</summary>
    private async Task ResetProfitPeriodAsync()
    {
        if (_result is null) return;

        // Kapsam-farkındalıklı onay: şube mi tüm şubeler mi (company-konsolide) net söylenir + GL yapılmayacağı uyarısı.
        string scopeText = _companyMode ? Loc["BalanceSheet:ResetScopeCompany"].Value : WorkingLabel();
        var message      = Loc["BalanceSheet:ResetConfirm", scopeText, _asOf.ToString("dd.MM.yyyy")];
        var confirm   = await Ui.ConfirmAsync(
            message.Value, Loc["BalanceSheet:ResetPeriod"].Value,
            Loc["BalanceSheet:ResetConfirmYes"].Value, Loc["Cancel"].Value, showCancel: false, defaultYes: false);
        if (confirm != ConfirmDialogResult.Yes) return;

        _busy = true;
        try
        {
            _result = await BalanceSheetReportAppService.ResetProfitPeriodAsync(BuildFilter());
            await LoadSnapshotsAsync();   // dönem sıfırlandı + snapshot alındı → geçmiş grid'i tazele.
            Ui.ShowSuccessToast(Loc["BalanceSheet:ResetDone"]);
        }
        finally { _busy = false; }
    }

    // Working şube/şirket değişince tab başlığı + etiket tazelenir (rapor manuel — "Bilanço Al" ile gelir).
    private void OnWorkingChanged()
    {
        UpdateTabTitle();
        _ = InvokeAsync(StateHasChanged);
    }

    private string WorkingLabel()
    {
        var b = Working.CurrentBranch;
        return b is null ? "—" : $"{b.CompanyDisplay} / {b.BranchDisplay}";
    }

    /// <summary>İşaret rengi: +(varlık) yeşil, −(borç) kırmızı, 0 nötr.</summary>
    private static string SignColor(decimal v)
    {
        return v > 0 ? "var(--dxbl-success, #2e7d32)" : v < 0 ? "var(--dxbl-danger, #c62828)" : "inherit";
    }

    /// <summary>Pozitif/negatif değer = KAR/ZARAR GRADYANLI rozet (yeşil/kırmızı dolu, beyaz yazı); 0 → düz. Banner ile aynı gradyan.</summary>
    private static string GradientBadgeStyle(decimal v)
    {
        return v == 0m
            ? "display:inline-block; padding:2px 6px;"
            : $"display:inline-block; padding:2px 8px; border-radius:4px; color:#fff; font-weight:600; background:{(v > 0m ? "var(--gradient-green)" : "var(--gradient-red)")};";
    }

    /// <summary>Kategori base-net (sol grup başlığı + sağ özet kolonları); kategori yoksa 0.</summary>
    private decimal CatNet(string category)
    {
        return _result?.CategoryTotals.FirstOrDefault(c => c.Category == category)?.Net ?? 0m;
    }

    private BalanceSheetCategoryTotalDto? CatTotal(string category)
    {
        return _result?.CategoryTotals.FirstOrDefault(c => c.Category == category);
    }

    /// <summary>Sağ grid SADECE kaydedilen bilanço snapshot'larını gösterir (ERPPRO üst grid); GetSnapshotListAsync ile dolar.</summary>
    private List<BilancoSummaryRow> _savedSnapshots = new();

    /// <summary>Snapshot geçmişi isteği — ComputeAsync/SaveAsync ile AYNI kapsam (company client'tan gönderilmez).</summary>
    private BalanceSheetSnapshotListRequestDto BuildSnapshotRequest()
    {
        return new()
        {
            Scope    = _companyMode ? BalanceSheetScope.Company : BalanceSheetScope.Branch,
            BranchId = _companyMode ? null : Working.CurrentBranchId,
        };
    }

    /// <summary>Kaydedilen bilanço geçmişini sunucudan çekip sağ grid'e (BilancoSummaryRow) map'ler. Kur farkı bu fazda 0.</summary>
    private async Task LoadSnapshotsAsync()
    {
        var list = await BalanceSheetReportAppService.GetSnapshotListAsync(BuildSnapshotRequest());
        _savedSnapshots = list.Rows.Select(r => new BilancoSummaryRow(
            Sube:     r.BranchCode ?? (r.Scope == BalanceSheetScope.Company ? Loc["BalanceSheet:Consolidated"].Value : string.Empty),
            Tarih:    r.AsOfDate,
            Gider:    r.CategoryNets.GetValueOrDefault(BalanceSheetCategory.Expense),
            Gelir:    r.CategoryNets.GetValueOrDefault(BalanceSheetCategory.Income),
            Gunluk:   r.Gunluk,
            MToplam:  r.Masraf,
            KurFarki: r.KurFarki,   // gün-aşırı yeniden değerleme (ERPPRO GetKurFarki; önceki snapshot pozisyonu bu günün kuruyla)
            Bakiye:   r.CategoryNets.GetValueOrDefault(BalanceSheetCategory.AccountBalance),
            Stok:     r.CategoryNets.GetValueOrDefault(BalanceSheetCategory.Stock),
            Pirlanta: r.CategoryNets.GetValueOrDefault(BalanceSheetCategory.Jewelry),   // ERPPRO PIRLANTA kolonu = bizde Jewelry (Mücevher)
            Tas:      r.CategoryNets.GetValueOrDefault(BalanceSheetCategory.Stone),
            Iscilik:  r.CategoryNets.GetValueOrDefault(BalanceSheetCategory.Labor),
            Takoz:    r.CategoryNets.GetValueOrDefault(BalanceSheetCategory.Bullion),
            Devir:    r.Devir,
            KarZarar: r.KarZarar,
            Toplam:   r.Total,
            Birim:    r.BaseCurrencyCode)).ToList();
    }

    /// <summary>Sol-alt KAR/ZARAR detay grid: TOPLAM'a giren kategorilerin birim-bazında net'i (DEVIR yokken KARZARAR = TOPLAM/birim).</summary>
    private List<KarZararRow> KarZararRows()
    {
        if (_result is null) return new();
        return _result.Rows
            .Where(r => CatTotal(r.Category)?.CountsInTotal ?? true)
            .GroupBy(r => r.UnitCode)
            .Select(g => new KarZararRow(g.Key ?? string.Empty, g.Sum(x => x.Amount), g.Sum(x => x.Net)))
            .Where(x => x.Bakiye != 0m || x.Net != 0m)
            .ToList();
    }

    public sealed record KarZararRow(string UnitCode, decimal Bakiye, decimal Net);

    public sealed record BilancoSummaryRow(
        string Sube, DateTime Tarih,
        decimal Gider, decimal Gelir, decimal Gunluk, decimal MToplam, decimal KurFarki,
        decimal Bakiye, decimal Stok, decimal Pirlanta, decimal Tas, decimal Iscilik, decimal Takoz,
        decimal Devir, decimal KarZarar, decimal Toplam, string Birim);

    public void Dispose()
    {
        Working.Changed -= OnWorkingChanged;
    }
}
