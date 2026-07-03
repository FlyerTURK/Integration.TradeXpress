using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Bullions;
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
    private object? _selectedLine;                                   // tek seçim (yürüyen bakiye + Düzelt için)
    private IReadOnlyList<object> _selectedLines = Array.Empty<object>();   // çoklu seçim (Sil + selection kolonu)
    private Guid? _currentVoucherId;
    private AccountSelectionPanel? _accountPanel;
    private bool _processActive;   // süreç paneli açıkken Düzelt/Sil toolbar gizli
    private bool _accountLocked;   // cari panel TAMAM ile kilitliyken Düzelt/Sil görünür

    // Liste modu (ekstre): cari'nin tarih aralığındaki tüm satırları (fiş-bağımsız). Sağ bakiye görünür kalır.
    private bool _listMode;
    private DateTime _listStart = DateTime.Today.AddDays(-7);
    private DateTime _listEnd   = DateTime.Today;

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
        _currentSubAccountId = sa?.Id;
        _voucherLines = new();   // farklı cari → satır gridini temizle

        // Sekme başlığı: cari seçilince 2 satır (L1=Hesap, L2=Alt hesap); seçim yoksa tek satır.
        if (CurrentMdiTab != null)
        {
            var header = sa is null
                ? new Integration.Framework.Blazor.Client.Services.Mdi.TabHeaderData { FormCaption = "Cari İşlemler" }
                : new Integration.Framework.Blazor.Client.Services.Mdi.TabHeaderData
                {
                    FormCaption = "Cari İşlemler",
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
            Ui.ShowWarningToast("Önce cari seçin.");
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
            Ui.ShowWarningToast("Doğrulama toplamı yanlış.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_deleteReason))
        {
            Ui.ShowWarningToast("Silme nedeni gerekli.");
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
        Ui.ShowSuccessToast(lines.Count == 1 ? "Satır silindi." : $"{lines.Count} satır silindi.");
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

    /// <summary>Görünen bakiye satırlarını canlı kurla hesabın bakiye birimine çevirip toplar.
    /// Kuru olmayan birim → toplama katılmaz, ≈ (eksik) işaretlenir.
    /// TAKOZ pseudo-birimin kuru yok — legacy <c>FN.TakozKur</c> paritesi: DefaultCarpan(0.6) × HAS kuru
    /// (Report.BakiyeListesi: <c>TAKOZ × 0.600 × KurHas</c>; 1000 TAKOZ → "600.00 HAS (A)").</summary>
    private void ComputeConsolidated()
    {
        decimal total = 0m;
        var complete = true;

        // Pivot (TRY) Buy — tutarlı tek-yön; parite görüntü fiyatı (_liveRates) konsolide matematiği için kullanılamaz.
        var buyById = _pivotBuy;
        var baseBuy = buyById.GetValueOrDefault(_baseUnitId);
        var hasBuy  = _liveRates.FirstOrDefault(p => p.CurrencyUnitCode == CurrencyUnitCode.HAS) is { } has
            ? buyById.GetValueOrDefault(has.Id)
            : 0m;

        foreach (var row in _balanceRows)
        {
            if (row.Net == 0m) continue;

            if (row.UnitId == _baseUnitId)
            {
                total += row.Net;                     // zaten base cinsinden
                continue;
            }

            // TAKOZ pseudo-birim: kur tablosunda yok → Carpan × HAS kuru ile değerle.
            var unitBuy = row.UnitId == BullionConsts.PseudoUnitId
                ? BullionConsts.DefaultCarpan * hasBuy
                : buyById.GetValueOrDefault(row.UnitId);
            if (unitBuy <= 0m || baseBuy <= 0m)
            {
                complete = false;                     // değerlenemedi
                continue;
            }

            total += row.Net * unitBuy / baseBuy;     // pivot üzerinden base'e çevir
        }

        _consTotal    = total;
        _consComplete = complete;
    }

    private string DirectionText(ProcessDirectionType d)
        => L[$"Enum:ProcessDirectionType:{d}"].Value;

    private string PaymentText(ProcessPaymentType? p)
        => p is { } v ? L[$"Enum:ProcessPaymentType:{v}"].Value : string.Empty;

    private List<CurrentPriceDto> _liveRates = new();
    private Dictionary<Guid, decimal> _pivotBuy = new();   // konsolide bakiye için TUTARLI pivot Buy (parite görüntü değil)
    private PeriodicTimer? _rateTimer;
    private CancellationTokenSource? _rateCts;   // dispose'da döngüyü iptal eder (tick↔dispose yarış penceresi)
    private DateTime _lastRateChangeUtc;         // render kapısı: flash animasyonu penceresi (son değişim anı)

    private readonly Dictionary<(Guid Id, bool Buy), decimal> _prevEffective = new();
    private readonly Dictionary<(Guid Id, bool Buy), (int Dir, DateTime Until)> _flash = new();

    private void TrackDirection(Guid id, bool buy, decimal value, DateTime now)
    {
        var key = (id, buy);
        if (_prevEffective.TryGetValue(key, out var prev) && value != prev)
            _flash[key] = (value > prev ? 1 : -1, now.AddSeconds(1));
        _prevEffective[key] = value;
    }

    protected string PriceCellStyle(Guid id, bool buy)
    {
        var on = _flash.TryGetValue((id, buy), out var f) && DateTime.UtcNow < f.Until;
        var bg = !on ? "transparent"
            : f.Dir > 0
                ? "var(--flash-green)"
                : "var(--flash-red)";
        return $"display:block; text-align:right; padding:2px 6px; border-radius:4px; background:{bg}; transition: background 700ms ease-out;";
    }

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

    private async Task LiveRateLoopAsync()
    {
        _rateCts   = new CancellationTokenSource();
        _rateTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await _rateTimer.WaitForNextTickAsync(_rateCts.Token))
            {
                var changed = await RefreshRatesAsync();
                // Render kapısı: kur değişmediyse (ve flash animasyon penceresi kapandıysa) tüm form
                // ağacını her saniye yeniden çizme — büyük grid'lerde gereksiz diff yükü.
                if (changed || DateTime.UtcNow - _lastRateChangeUtc < TimeSpan.FromSeconds(1.5))
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }   // tick ↔ dispose yarış penceresi (circuit kapanışı) — sessiz çık
    }

    /// <summary>Kurları tazeler; en az bir fiyat DEĞİŞTİYSE true döner (render kapısı için).</summary>
    private async Task<bool> RefreshRatesAsync()
    {
        var changed = false;
        try
        {
            var prices = await PriceService.GetCurrentPricesAsync();   // YEREL para birimine re-base'li (ülke parası); bilanço değil
            var now = DateTime.UtcNow;
            foreach (var p in prices)
            {
                var old = _liveRates.FirstOrDefault(x => x.Id == p.Id);
                if (old is null || old.Buy != p.Buy || old.Sell != p.Sell)
                {
                    changed = true;
                }
                TrackDirection(p.Id, buy: true, p.Buy, now);
                TrackDirection(p.Id, buy: false, p.Sell, now);
            }
            _liveRates = prices;
            // Konsolide bakiye matematiği aynı fiyat listesinden — İKİNCİ servis çağrısı GEREKMEZ
            // (eski kod aynı metodu tik başına iki kez çağırıyordu; canlı yol pahalı — teke indirildi).
            _pivotBuy = prices.ToDictionary(p => p.Id, p => p.Buy);
            if (changed)
            {
                _lastRateChangeUtc = now;
            }
        }
        catch { }
        ComputeConsolidated();   // konsolide toplam canlı kurla tazelensin
        return changed;
    }

    public void Dispose()
    {
        _rateCts?.Cancel();
        _rateCts?.Dispose();
        _rateTimer?.Dispose();
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
