using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

public partial class CurrentTransactionForm
{
    private bool _isMobile;

    // VoucherLines — kaydedilen fişin satırları (p2 grid).
    private List<VoucherLineDto> _voucherLines = new();

    /// <summary>Takoz kolonları (rapor no/çeşni/Ag-Pt-Pd milyem) yalnız görünümde takoz satırı varken açılır.</summary>
    private bool _hasBullionRows
    {
        get { return _voucherLines.Any(l => l.Type == ProcessType.Bullion); }
    }
    private object? _selectedLine;                                   // tek seçim (yürüyen bakiye + Düzelt için)
    private IReadOnlyList<object> _selectedLines = Array.Empty<object>();   // çoklu seçim (Sil + selection kolonu)
    private Guid? _currentVoucherId;
    private AccountSelectionPanel? _accountPanel;
    private bool _processActive;   // süreç paneli açıkken Düzelt/Sil toolbar gizli
    private bool _accountLocked;   // cari panel TAMAM ile kilitliyken Düzelt/Sil görünür

    // Liste modu (ekstre): cari'nin tarih aralığındaki tüm satırları (fiş-bağımsız). Sağ bakiye görünür kalır.
    private bool _listMode;
    private DateTime _listStart = BusinessClock.Today().AddDays(-7);
    private DateTime _listEnd   = BusinessClock.Today();

    // Ekstre eklentileri: devreden (grid üstü) + kapanış (grid altı) + işlem-tipi filtresi + Excel export.
    private List<VoucherBalanceLineDto> _listOpening = new();
    private List<VoucherBalanceLineDto> _listClosing = new();
    private IEnumerable<ProcessType> _listTypes = Enumerable.Empty<ProcessType>();
    private List<ProcessTypeItem> _processTypeItems = new();
    private IGrid? _listGrid;

    /// <summary>İşlem-tipi çoklu-seçim öğesi (record: TagBox item karşılaştırması Equals ister).</summary>
    private sealed record ProcessTypeItem(ProcessType Value, string Text);

    // Bakiye (p3 Bakiye sekmesi) — anlık hesap.
    private List<VoucherBalanceLineDto> _balanceRows = new();
    private Guid? _currentSubAccountId;

    // Konsolide toplam — hesabın bakiye birimi cinsinden, canlı kurla (her tikte).
    private Guid    _baseUnitId;
    private string  _baseCode = string.Empty;
    private decimal _consTotal;
    private bool    _consComplete = true;

    private async Task OnLineSaved(Guid voucherId)
    {
        _currentVoucherId = voucherId;
        if (_listMode)
            await ReloadListAsync();   // liste modundayken listeyi yenile, moddan çıkma
        else
            _voucherLines = await VoucherService.GetLinesAsync(voucherId);
        _selectedLine = null;
        await RefreshBalanceAsync();
        await InvokeAsync(StateHasChanged);
    }

    [CascadingParameter(Name = "CurrentMdiTab")]
    private Integration.Framework.Blazor.Client.Services.Mdi.IMdiTab? CurrentMdiTab { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "subAccountId")]
    public Guid? SubAccountId { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "voucherId")]
    public Guid? VoucherId { get; set; }

    private void PushStateToUrl()
    {
        if (CurrentMdiTab == null) return;
        var q = new List<string>();
        if (_currentSubAccountId.HasValue) q.Add($"subAccountId={_currentSubAccountId.Value}");
        if (_currentVoucherId.HasValue) q.Add($"voucherId={_currentVoucherId.Value}");

        var url = "/cari-islemler";
        if (q.Any()) url += "?" + string.Join("&", q);
        Tabs.UpdateTabUrl(CurrentMdiTab.Id, url);
    }

    private async Task OnSubAccountSelected(SubAccountListDto? sa)
    {
        // Aynı cari BAŞKA bir Cari İşlemler sekmesinde zaten açıksa: seçimi geri al, o sekmeye geç —
        // aynı carinin ikinci sekmesi açılmaz (URL'deki subAccountId üzerinden; PushStateToUrl tek kaynak).
        if (sa != null && CurrentMdiTab != null)
        {
            var needle = $"subAccountId={sa.Id}";
            var existing = Tabs.Tabs.FirstOrDefault(t =>
                t.Id != CurrentMdiTab.Id
                && t.Url.StartsWith("/cari-islemler", StringComparison.OrdinalIgnoreCase)
                && t.Url.Contains(needle, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (_accountPanel != null)
                {
                    await _accountPanel.ClearSubAccountSelectionAsync();   // bu sekme boş kalır
                }

                Ui.ShowWarningToast(L["SubAccountAlreadyOpenInAnotherTab"]);
                Tabs.Activate(existing.Id);
                return;
            }
        }

        _currentSubAccountId = sa?.Id;
        _voucherLines = new();   // farklı cari → satır gridini temizle

        // Sekme başlığı: cari seçilince 2 satır (L1=Hesap, L2=Alt hesap); seçim yoksa tek satır.
        if (CurrentMdiTab != null)
        {
            var header = sa is null
                ? new Integration.Framework.Blazor.Client.Services.Mdi.TabHeaderData { FormCaption = L["Menu:CurrentTransactions"] }
                : new Integration.Framework.Blazor.Client.Services.Mdi.TabHeaderData
                {
                    FormCaption = L["Menu:CurrentTransactions"],
                    // Sekme dar alan — yalnız KODLAR (Code/Name değil; kullanıcı isteği). Diğer kullanım
                    // yerleri etkilenmez: TabHeaderData altyapısı aynı, yalnız bu formun değerleri kısaldı.
                    EntityValue = sa.Code,
                    ParentLabel = L["Entity:Account"],
                    ParentValue = sa.AccountCode,
                    IconCssClass = "custom-icon-swap",
                };
            Tabs.UpdateTabHeader(CurrentMdiTab.Id, header);
        }

        await RefreshBalanceAsync();
        PushStateToUrl();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnVoucherOpened(Guid? voucherId)
    {
        // TAMAM → seçili fişin hareketlerini göster; yeni fiş/HESAP SEÇ → boşalt.
        _currentVoucherId = voucherId;
        _voucherLines = voucherId is { } id
            ? await VoucherService.GetLinesAsync(id)
            : new List<VoucherLineDto>();
        _selectedLine = null;
        await RefreshBalanceAsync();   // seçim yok → cari toplamı
        PushStateToUrl();
        await InvokeAsync(StateHasChanged);
    }

    private async Task EnterListModeAsync()
    {
        if (_currentSubAccountId is null)
        {
            Ui.ShowWarningToast(L["SelectSubAccountFirst"]);
            return;
        }
        _listMode = true;
        _selectedLine = null;
        await ReloadListAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnListRangeChanged(DateTime start, DateTime end)
    {
        _listStart = start;
        _listEnd   = end;
        await ReloadListAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task ReloadListAsync()
    {
        if (_currentSubAccountId is { } id)
        {
            var statement = await VoucherService.GetAccountStatementAsync(
                id, _listStart.Date, _listEnd.Date.AddDays(1), _listTypes.ToList());
            _voucherLines = statement.Lines;
            _listOpening  = statement.OpeningBalances;
            _listClosing  = statement.ClosingBalances;
        }
        else
        {
            _voucherLines = new List<VoucherLineDto>();
            _listOpening  = new List<VoucherBalanceLineDto>();
            _listClosing  = new List<VoucherBalanceLineDto>();
        }
    }

    private async Task OnListTypesChanged(IEnumerable<ProcessType> values)
    {
        _listTypes = values ?? Enumerable.Empty<ProcessType>();
        await ReloadListAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task ExportListToExcelAsync()
    {
        // Server'da loader no-op; WASM'a taşınırsa export assembly'leri lazy yüklenir (DrillList ile aynı yol).
        await ExportLoader.EnsureLoadedAsync();
        var name = $"{L["TransactionList"].Value} {_listStart:yyyy-MM-dd} {_listEnd:yyyy-MM-dd}";
        await _listGrid.ExportToXlsxSafeAsync(name);
    }

    private async Task ExitListModeAsync()
    {
        _listMode = false;
        _selectedLine = null;
        _voucherLines = _currentVoucherId is { } vid
            ? await VoucherService.GetLinesAsync(vid)
            : new List<VoucherLineDto>();
        await RefreshBalanceAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnEditLine()
    {
        if (_selectedLine is VoucherLineDto line && _accountPanel is not null)
        {
            var dto = await VoucherService.GetLineForEditAsync(line.Id);
            // _processActive true olunca p1 paneli liste modunda da görünür hale gelir.
            await _accountPanel.BeginEditLineAsync(dto);
        }
    }

    // ── Silme onayı (rakam doğrulama + neden) ──
    // Random.Shared: thread-safe — static kendi Random'ımız çok-circuit paralel Next()'te bozulabilirdi.
    private static Random _rng => Random.Shared;
    private bool   _showDeleteDialog;
    private int    _deleteA, _deleteB;
    private int?   _deleteAnswer;
    private string _deleteReason = string.Empty;

    private void OnDeleteLine()
    {
        if (_selectedLines.Count == 0)
            return;

        _deleteA      = _rng.Next(1, 10);
        _deleteB      = _rng.Next(1, 10);
        _deleteAnswer = null;
        _deleteReason = string.Empty;
        _showDeleteDialog = true;
    }

    private async Task ConfirmDeleteAsync()
    {
        var lines = _selectedLines.OfType<VoucherLineDto>().Where(l => l.VoucherId is not null).ToList();
        if (lines.Count == 0)
            return;

        if (_deleteAnswer != _deleteA + _deleteB)
        {
            Ui.ShowWarningToast(L["VerificationSumIncorrect"]);
            return;
        }
        if (string.IsNullOrWhiteSpace(_deleteReason))
        {
            Ui.ShowWarningToast(L["DeleteReasonRequired"]);
            return;
        }

        _showDeleteDialog = false;
        var reason = _deleteReason.Trim();
        foreach (var line in lines)
            await VoucherService.DeleteLineAsync(line.VoucherId!.Value, line.Id, reason);

        if (_listMode)
            await ReloadListAsync();
        else if (_currentVoucherId is { } vid)
            _voucherLines = await VoucherService.GetLinesAsync(vid);

        _selectedLines = Array.Empty<object>();
        _selectedLine = null;
        await RefreshBalanceAsync();
        await InvokeAsync(StateHasChanged);
        Ui.ShowSuccessToast(lines.Count == 1 ? L["LineDeleted"] : L["LinesDeleted", lines.Count]);
    }

    private async Task OnLineSelected(object? item)
    {
        _selectedLine = item;
        if (item is VoucherLineDto line)
        {
            _balanceRows = line.RunningBalances;   // seçili satıra kadarki yürüyen bakiye
            ComputeConsolidated();
        }
        else
        {
            await RefreshBalanceAsync();            // seçim kalktı → cari toplamı
        }
        await InvokeAsync(StateHasChanged);
    }

    // Çoklu-seçim → tek seçim türet: TAM 1 satır seçiliyse onun yürüyen bakiyesi + Düzelt aktif; aksi halde toplam.
    private async Task OnLinesSelected(IReadOnlyList<object> items)
    {
        _selectedLines = items ?? Array.Empty<object>();
        await OnLineSelected(_selectedLines.Count == 1 ? _selectedLines[0] : null);
    }

    private async Task RefreshBalanceAsync()
    {
        if (_currentSubAccountId is { } id)
        {
            var res = await VoucherService.GetBalancesAsync(id);
            _balanceRows  = res.Lines;
            _baseUnitId   = res.BalanceUnitId;
            _baseCode     = res.BalanceCode;
        }
        else
        {
            _balanceRows = new List<VoucherBalanceLineDto>();
            _baseUnitId  = Guid.Empty;
            _baseCode    = string.Empty;
        }
        ComputeConsolidated();
    }

    /// <summary>Görünen bakiye satırlarını canlı kurla hesabın bakiye birimine çevirip toplar
    /// (saf hesap <see cref="ConsolidatedBalanceCalculator"/>'da — form yalnız girdileri toplayıp çağırır).</summary>
    private void ComputeConsolidated()
    {
        // Pivot (TRY) Buy — tutarlı tek-yön; parite görüntü fiyatı (_liveRates) konsolide matematiği için kullanılamaz.
        var hasBuy = _liveRates.FirstOrDefault(p => p.CurrencyUnitCode == CurrencyUnitCode.HAS) is { } has
            ? _pivotBuy.GetValueOrDefault(has.Id)
            : 0m;

        var result = ConsolidatedBalanceCalculator.Calculate(_balanceRows, _baseUnitId, _pivotBuy, hasBuy);
        _consTotal    = result.Total;
        _consComplete = result.IsComplete;
    }

    private string DirectionText(ProcessDirectionType d)
        => L[$"Enum:ProcessDirectionType:{d}"].Value;

    private string PaymentText(ProcessPaymentType? p)
        => p is { } v ? L[$"Enum:ProcessPaymentType:{v}"].Value : string.Empty;

    // Canlı kur alanları + döngüsü CurrentTransactionForm.LiveRates.cs partial dosyasında.

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        // İşlem-tipi filtresi (ekstre) seçenekleri — enum + lokalize ad.
        _processTypeItems = Enum.GetValues<ProcessType>()
            .Select(t => new ProcessTypeItem(t, L[$"Enum:ProcessType:{t}"].Value))
            .ToList();
        await RefreshRatesAsync();
        _ = LiveRateLoopAsync();
    }

    private string GridStyle()
    {
        if (!_currentSubAccountId.HasValue)
        {
            return _isMobile
                ? "display:grid; gap:0px; grid-template-columns:1fr; grid-template-areas:'p1'; overflow-y:auto; max-height:calc(100vh - 110px);"
                : "display:grid; gap:0px; height:calc(100vh - 110px); grid-template-columns:1fr; grid-template-areas:'p1';";
        }

        if (!_accountLocked)
        {
            return _isMobile
                ? "display:grid; gap:0px; grid-template-columns:1fr; grid-template-areas:'p1' 'p3'; overflow-y:auto; max-height:calc(100vh - 110px);"
                : "display:grid; gap:0px; height:calc(100vh - 110px); grid-template-columns:minmax(0,1fr) 300px; grid-template-areas:'p1 p3';";
        }

        if (_isMobile)
            return _listMode && !_processActive
                ? "display:grid; gap:0px; grid-template-columns:1fr; grid-template-areas:'p3' 'p2'; overflow-y:auto; max-height:calc(100vh - 110px);"
                : "display:grid; gap:0px; grid-template-columns:1fr; grid-template-areas:'p1' 'p3' 'p2'; overflow-y:auto; max-height:calc(100vh - 110px);";

        return _listMode && !_processActive
            ? "display:grid; gap:0px; height:calc(100vh - 110px); grid-template-columns:minmax(0,1fr) 300px; grid-template-areas:'p2 p3';"
            : "display:grid; gap:0px; height:calc(100vh - 110px); grid-template-columns:minmax(0,1fr) 300px; grid-template-rows:auto 1fr; grid-template-areas:'p1 p3' 'p2 p3';";
    }

    private static string PanelBox() =>
        "box-sizing:border-box; border-radius:12px; padding:4px; min-width:0;";
}
