using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.Framework.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>Bakiye Gösterim Modu (2026-07-15 kullanıcı kararı) — Bakiye sekmesindeki grid'in kapsamı.
/// <b>SubAccountScoped</b> = seçili tek alt hesap/kasa (bugünkü davranış) · <b>AccountScoped</b> = seçili
/// alt hesabın/kasanın bağlı olduğu cari hesabın/şubenin TÜM alt hesaplarının/kasalarının KONSOLİDE toplamı.</summary>
public enum BalanceViewMode
{
    SubAccountScoped,
    AccountScoped,
}

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
    private VoucherLineAttachmentsDialog? _attachmentsDialog;
    private bool _processActive;   // süreç paneli açıkken Düzelt/Sil toolbar gizli
    private bool _accountLocked;   // cari panel TAMAM ile kilitliyken Düzelt/Sil görünür

    /// <summary>Sekme kapanış guard'ı (cache'li delegate — EntityEditForm deseni): süreç paneli açıkken
    /// sekme X'i devam eden satır girişini onaysız kapatamasın.</summary>
    private Func<Task<bool>>? _tabCloseGuard;

    /// <summary>Restore edilen sekmenin görünüm durumu — liste kipi/bakiye kapsamı cari seçimi
    /// tamamlanınca uygulanır (OnSubAccountSelected varsayılana çektiği için bekletilir).</summary>
    private CurrentTransactionTabState? _pendingTabStateRestore;

    // Liste modu (ekstre): cari'nin tarih aralığındaki tüm satırları (fiş-bağımsız). Sağ bakiye görünür kalır.
    private bool _listMode;
    private DateTime _listStart = BusinessClock.Today().AddDays(-7);
    private DateTime _listEnd   = BusinessClock.Today();

    // Ekstre eklentileri: devreden (grid üstü) + kapanış (grid altı) + işlem-tipi filtresi + Excel export.
    private List<VoucherBalanceLineDto> _listOpening = new();
    /// <summary>Ekstre kapanış bakiyeleri — grid altındaki "son durum" şeridi 2026-07-26'da KALDIRILDI
    /// (yerini seçili satırın açıklaması aldı; aynı bilgi Bakiye sekmesinde duruyor). Alan doldurulmaya
    /// devam ediyor: sunucu zaten aynı sorguda döndürüyor ve şerit geri istenirse hazır.</summary>
    private List<VoucherBalanceLineDto> _listClosing = new();
    private IEnumerable<ProcessType> _listTypes = Enumerable.Empty<ProcessType>();
    private List<ProcessTypeItem> _processTypeItems = new();
    private IGrid? _listGrid;

    /// <summary>İşlem-tipi çoklu-seçim öğesi (record: TagBox item karşılaştırması Equals ister).</summary>
    private sealed record ProcessTypeItem(ProcessType Value, string Text);

    // Bakiye (p3 Bakiye sekmesi) — anlık hesap.
    private List<VoucherBalanceLineDto> _balanceRows = new();
    private Guid? _currentSubAccountId;
    private Guid? _currentAccountId;   // seçili alt hesabın/kasanın bağlı olduğu Account/Şube — AccountScoped kaynağı
    private BalanceViewMode _balanceViewMode = BalanceViewMode.SubAccountScoped;

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
        await ReloadLineHistoryAsync();
        await InvokeAsync(StateHasChanged);
    }

    [CascadingParameter(Name = "CurrentMdiTab")]
    private IMdiTab? CurrentMdiTab { get; set; }

    /// <summary>Görünüm durumunu sekmeye itmek için (TabPageState.Write) — TabManager'ın framework yüzü.</summary>
    [Inject] private IMdiTabOpener TabStateWriter { get; set; } = default!;

    /// <summary>Karşı taraf kipi — ROTADAN gelir, form içinde seçilmez (kullanıcı kararı: karşı-taraf
    /// combo'su kaldırıldı). <c>/cari-islemler</c> doğrudan bu formu çizer → varsayılan dış cari kipi;
    /// <c>/transfers</c> ise <see cref="TransferTransactionPage"/> üzerinden InternalVault verir.</summary>
    [Parameter]
    public CounterpartyMode Mode { get; set; } = CounterpartyMode.CurrentAccount;

    private bool IsInternalMode => Mode == CounterpartyMode.InternalVault;

    /// <summary>Sekme URL'sinin kök rotası — kip hangi rotadan geldiyse o (PushStateToUrl sekmeyi
    /// yanlış rotaya yazmasın: iç kipteki sekme /transfers olarak kalmalı).</summary>
    private string RouteBase => IsInternalMode ? "/transfers" : "/cari-islemler";

    /// <summary>Sekme başlığı/ikonu kipe göre (menüyle hizalı: Transferler vs Cari İşlemler).</summary>
    private string ModeCaption => IsInternalMode ? L["Menu:Transfers"] : L["Menu:CurrentTransactions"];

    /// <summary>Başlığın lokalizasyon anahtarı — sekme restore'unda güncel kültürle çözülür (dil donması yok).</summary>
    private string ModeCaptionKey => IsInternalMode ? "Menu:Transfers" : "Menu:CurrentTransactions";

    private string ModeIcon => IsInternalMode ? TradeXpressIcons.Transfer : TradeXpressIcons.CurrentTransactions;

    [Parameter]
    [SupplyParameterFromQuery(Name = "subAccountId")]
    public Guid? SubAccountId { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "voucherId")]
    public Guid? VoucherId { get; set; }

    private void OnProcessActiveChanged(bool active)
    {
        _processActive = active;
        // Süreç paneli açık = devam eden satır girişi → sekme kirli: kapanış onaya tabi, kalıcı kayıtta
        // WasDirty işaretlenir (restore'da "form verisi geri yüklenemedi" bildirimi).
        if (CurrentMdiTab != null)
            Tabs.SetTabDirty(CurrentMdiTab.Id, active);
    }

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        // Her render'da bağlanır (cache'li delegate): sekme yeniden aktive olup guard temizlense de geri gelir.
        _tabCloseGuard ??= ConfirmTabCloseAsync;
        if (CurrentMdiTab != null)
            CurrentMdiTab.CanCloseAsync = _tabCloseGuard;
    }

    private async Task<bool> ConfirmTabCloseAsync()
    {
        if (!_processActive) return true;
        var result = await Ui.ConfirmAsync(
            L["ProcessPanelDiscardConfirmation"].Value,
            L["Warning"].Value,
            L["CloseAnyway"].Value,
            L["Cancel"].Value, false, false);
        return result == ConfirmDialogResult.Yes;
    }

    /// <summary>Bileşen yok edilirken guard bırakılır (yalnız hâlâ bizimkiyse) — bayat delege kalmasın.</summary>
    private void ReleaseTabCloseGuard()
    {
        if (CurrentMdiTab != null && ReferenceEquals(CurrentMdiTab.CanCloseAsync, _tabCloseGuard))
            CurrentMdiTab.CanCloseAsync = null;
    }

    private void PushStateToUrl()
    {
        if (CurrentMdiTab == null) return;
        var q = new List<string>();
        if (_currentSubAccountId.HasValue) q.Add($"subAccountId={_currentSubAccountId.Value}");
        if (_currentVoucherId.HasValue) q.Add($"voucherId={_currentVoucherId.Value}");

        var url = RouteBase;
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
                && t.Url.StartsWith(RouteBase, StringComparison.OrdinalIgnoreCase)
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
        _currentAccountId    = sa?.AccountId;
        _balanceViewMode      = BalanceViewMode.SubAccountScoped;   // yeni cari seçimi → varsayılana dön
        _voucherLines = new();   // farklı cari → satır gridini temizle

        // Restore edilen görünüm durumu: yalnız GERÇEK cari seçiminde (sa dolu) tüketilir. AccountSelectionPanel
        // restore akışında InitialSubAccountId'i uygulamadan ÖNCE bilinçli bir "temizle" çağrısı yapar
        // (OnSubAccountChanged → OnSubAccountSelected.InvokeAsync(null)); pending o ara null geçişte
        // YOK EDİLİRSE gerçek seçim (OnSubAccountLostFocus ile) geldiğinde artık boş bulunur ve ListMode/
        // BalanceViewMode restore'u hiç uygulanmaz. Bu yüzden alan yalnız sa dolu geldiğinde temizlenir.
        var pendingRestore = sa is not null ? _pendingTabStateRestore : null;
        if (sa is not null)
            _pendingTabStateRestore = null;
        if (pendingRestore is not null)
            _balanceViewMode = pendingRestore.BalanceViewMode;

        // Sekme başlığı: cari seçilince 2 satır (L1=Hesap, L2=Alt hesap); seçim yoksa tek satır.
        if (CurrentMdiTab != null)
        {
            var header = sa is null
                ? new TabHeaderData
                {
                    FormCaption = ModeCaption,
                    FormCaptionKey = ModeCaptionKey,
                    IconCssClass = ModeIcon,
                }
                : new TabHeaderData
                {
                    FormCaption = ModeCaption,
                    FormCaptionKey = ModeCaptionKey,
                    // Sekme dar alan — yalnız KODLAR (Code/Name değil; kullanıcı isteği). Diğer kullanım
                    // yerleri etkilenmez: TabHeaderData altyapısı aynı, yalnız bu formun değerleri kısaldı.
                    EntityValue = sa.Code,
                    ParentLabel = L["Entity:Account"],
                    ParentLabelKey = "Entity:Account",
                    ParentValue = sa.AccountCode,
                    // Tab başlığı ikonu menü/entity ile hizalı (hardcoded custom-icon-swap değil; merkezî sabit).
                    IconCssClass = ModeIcon,
                };
            Tabs.UpdateTabHeader(CurrentMdiTab.Id, header);
        }

        await RefreshBalanceAsync();
        await ReloadLineHistoryAsync();

        // Restore edilen sekme liste kipindeydi: cari kimliği yerine oturduğuna göre ekstre görünümüne dön.
        if (pendingRestore is { ListMode: true } && !_listMode)
        {
            _listMode = true;
            _selectedLine = null;
            await ReloadListAsync();
        }

        PushStateToUrl();
        PushTabState();
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
        PushTabState();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnListRangeChanged(DateTime start, DateTime end)
    {
        _listStart = start;
        _listEnd   = end;
        await ReloadListAsync();
        PushTabState();
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
        PushTabState();
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
        PushTabState();
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

    /// <summary>İşlem geçmişi satırına çift tıklama → o kaydın anlık görüntüsünü SALT-OKUNUR panelde açar.
    /// Snapshot satırın o günkü tam hâlidir (<see cref="VoucherLineDto"/>), dönüşüm gerekmez.</summary>
    private async Task OnHistoryRowDoubleClick(GridRowClickEventArgs e)
    {
        if (e.Grid.GetDataItem(e.VisibleIndex) is not VoucherLineHistoryDto row || _accountPanel is null)
        {
            return;
        }

        // Geçmiş kaydı SİLİNMİŞ satıra da ait olabilir — panel yalnız gösterir, kaydetmez; sorun değil.
        await _accountPanel.BeginViewLineAsync(row.Snapshot);
    }

    // Seçili satırın ek SAYILARI — toolbar düğmelerinde "(2)" rozetiyle gösterilir ki kullanıcı
    // pencereyi açmadan içerik olup olmadığını görsün. Satır değişince tazelenir; kaydedilmemiş
    // satırda (Id boş) sorgu yapılmaz.
    private int _selectedLineDocumentCount;
    private int _selectedLineNoteCount;

    private async Task RefreshSelectedLineAttachmentCountsAsync(VoucherLineDto? line)
    {
        if (line is null || line.Id == Guid.Empty)
        {
            _selectedLineDocumentCount = 0;
            _selectedLineNoteCount = 0;
            return;
        }

        try
        {
            var entityName = VoucherLineAttachmentsDialog.VoucherLineEntityName;
            _selectedLineDocumentCount = (await DocumentAppService.GetForAsync(entityName, line.Id)).Count;
            _selectedLineNoteCount = (await NoteAppService.GetForAsync(entityName, line.Id)).Count;
        }
        catch
        {
            // Rozet ikincil bilgidir: sayım alınamazsa satır seçimi bozulmasın, rozet gizlenir.
            _selectedLineDocumentCount = 0;
            _selectedLineNoteCount = 0;
        }
    }

    /// <summary>Ek penceresi kaydedip kapandıktan sonra rozetleri tazeler.</summary>
    private async Task RefreshAttachmentBadgesAsync()
    {
        await RefreshSelectedLineAttachmentCountsAsync(_selectedLine as VoucherLineDto);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Seçili satırın açıklaması — doluysa "AÇIKLAMA: ..." önekiyle gösterilir, boşsa <c>null</c>
    /// döner ki kontrolün ipucu metni ("AÇIKLAMA") görünsün. Önek yalnız GÖSTERİMDEDİR; kayıtlı veri değişmez
    /// (alan salt-okunur, düzenleme satır panelinden yapılır).</summary>
    private string? FormatLineDescription(string? description)
        => string.IsNullOrWhiteSpace(description)
            ? null
            : $"{L["Description"].Value.ToUpper()}: {description}";

    /// <summary>Düğme metni: kayıt varsa "Dokümanlar (2)", yoksa sade "Dokümanlar".</summary>
    private string AttachmentButtonText(string caption, int count)
        => count > 0 ? $"{caption} ({count})" : caption;

    // Satır ekleri: seri numarası, kamera kaydı, kargo/sigorta evrakı. Dokümanlar ve Notlar AYRI düğme,
    // her biri kendi penceresini açar. Ek satırın KİMLİĞİNE bağlandığı için yalnız kaydedilmiş satırda
    // açılır (toolbar Enabled koşulu bunu zaten garanti eder).
    private Task OnLineDocuments()
        => OpenLineAttachmentsAsync(VoucherLineAttachmentsDialog.AttachmentMode.Documents);

    private Task OnLineNotes()
        => OpenLineAttachmentsAsync(VoucherLineAttachmentsDialog.AttachmentMode.Notes);

    private async Task OpenLineAttachmentsAsync(VoucherLineAttachmentsDialog.AttachmentMode mode)
    {
        if (_selectedLine is VoucherLineDto line && line.Id != Guid.Empty && _attachmentsDialog is not null)
        {
            var title = $"{line.CommodityCode} — {line.Amount:N2} {line.MainUnitCode}".Trim();
            await _attachmentsDialog.OpenAsync(line.Id, title, mode);
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
        await ReloadLineHistoryAsync();
        await InvokeAsync(StateHasChanged);
        Ui.ShowSuccessToast(lines.Count == 1 ? L["LineDeleted"] : L["LinesDeleted", lines.Count]);
    }

    private async Task OnLineSelected(object? item)
    {
        _selectedLine = item;
        await RefreshSelectedLineAttachmentCountsAsync(item as VoucherLineDto);
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

    /// <summary>Bakiye Gösterim Modu değişti: seçili cari için kapsamı yeniden çeker.</summary>
    private async Task OnBalanceViewModeChanged(bool accountScoped)
    {
        _balanceViewMode = accountScoped ? BalanceViewMode.AccountScoped : BalanceViewMode.SubAccountScoped;
        await RefreshBalanceAsync();
        PushTabState();
        await InvokeAsync(StateHasChanged);
    }

    // ── Birim tarihçesi popup'ı (Bakiye satırına çift-tık) ──
    private bool             _showUnitStatement;
    private Guid              _unitStatementUnitId;
    private string            _unitStatementUnitCode = string.Empty;
    private DateTime          _unitStatementStart = BusinessClock.Today().AddDays(-7);
    private DateTime          _unitStatementEnd   = BusinessClock.Today();
    private UnitStatementDto? _unitStatement;

    /// <summary>Bakiye gridinde bir birime çift-tıklanınca o birimin tarihçe popup'ını açar — kapsam Bakiye
    /// Gösterim Modu'yla aynıdır (SubAccountScoped → seçili alt hesap/kasa, AccountScoped → bağlı hesap/şube).</summary>
    private async Task OnBalanceRowDoubleClick(DevExpress.Blazor.GridRowClickEventArgs e)
    {
        if (e.Grid.GetDataItem(e.VisibleIndex) is not VoucherBalanceLineDto row)
        {
            return;
        }

        _unitStatementUnitId   = row.UnitId;
        _unitStatementUnitCode = row.UnitCode;
        _unitStatementStart    = BusinessClock.Today().AddDays(-7);
        _unitStatementEnd      = BusinessClock.Today();
        _showUnitStatement     = true;
        await ReloadUnitStatementAsync();
    }

    // ── Log sekmesi (fiş satırı değişim günlüğü) + satır-tarihçesi popup'ı ──
    private List<VoucherLineHistoryDto> _lineHistory = new();
    private DateTime _historyStart = BusinessClock.Today().AddDays(-7);
    private DateTime _historyEnd   = BusinessClock.Today();

    private bool _showLineHistory;
    private List<VoucherLineHistoryDto> _selectedLineHistory = new();

    /// <summary>Rozet rengi — Confirmation grid'in (StatusBadgeStyle) görsel diliyle hizalı: yeşil=eklendi,
    /// mavi=güncellendi, kırmızı=silindi.</summary>
    private static string ChangeTypeBadgeStyle(VoucherLineChangeType changeType)
    {
        var background = changeType switch
        {
            VoucherLineChangeType.Created => "#16a34a",
            VoucherLineChangeType.Updated => "#3b82f6",
            VoucherLineChangeType.Deleted => "#dc2626",
            _ => "#6b7280",
        };
        return $"display:inline-block; padding:2px 8px; border-radius:10px; font-size:12px; font-weight:600; color:#fff; background:{background};";
    }

    private async Task OnHistoryRangeChanged(DateTime start, DateTime end)
    {
        _historyStart = start;
        _historyEnd   = end;
        await ReloadLineHistoryAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task ReloadLineHistoryAsync()
    {
        _lineHistory = _currentSubAccountId is { } id
            ? await VoucherLineHistoryService.GetBySubAccountAsync(id, _historyStart.Date, _historyEnd.Date.AddDays(1))
            : new List<VoucherLineHistoryDto>();
    }

    /// <summary>Fiş satırı gridinde bir satıra çift-tıklanınca o SATIRIN tam tarihçesini popup'ta gösterir.</summary>
    private async Task OnVoucherLineRowDoubleClick(DevExpress.Blazor.GridRowClickEventArgs e)
    {
        if (e.Grid.GetDataItem(e.VisibleIndex) is not VoucherLineDto row || row.Id == Guid.Empty)
        {
            return;
        }

        _selectedLineHistory = await VoucherLineHistoryService.GetByLineAsync(row.Id);
        _showLineHistory = true;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnUnitStatementRangeChanged(DateTime start, DateTime end)
    {
        _unitStatementStart = start;
        _unitStatementEnd   = end;
        await ReloadUnitStatementAsync();
    }

    private async Task ReloadUnitStatementAsync()
    {
        var scopeIsAccount = _balanceViewMode == BalanceViewMode.AccountScoped;
        var scopeId = scopeIsAccount ? _currentAccountId : _currentSubAccountId;
        if (scopeId is not { } id)
        {
            _unitStatement = null;
            return;
        }

        _unitStatement = await VoucherService.GetUnitStatementAsync(
            id, scopeIsAccount, _unitStatementUnitId, _unitStatementStart.Date, _unitStatementEnd.Date.AddDays(1));
        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshBalanceAsync()
    {
        if (_balanceViewMode == BalanceViewMode.AccountScoped && _currentAccountId is { } accountId)
        {
            var res = await VoucherService.GetAccountBalancesAsync(accountId);
            _balanceRows  = res.Lines;
            _baseUnitId   = res.BalanceUnitId;
            _baseCode     = res.BalanceCode;
        }
        else if (_currentSubAccountId is { } id)
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

        // Restore edilen sekmenin görünüm durumu: tarih/tip filtresi hemen uygulanır; liste kipi ve
        // bakiye kapsamı cari seçimi tamamlanınca (OnSubAccountSelected → pending) devreye girer.
        _pendingTabStateRestore = TabPageState.TryRead<CurrentTransactionTabState>(CurrentMdiTab);
        if (_pendingTabStateRestore is { } restored)
        {
            _listStart = restored.ListStart;
            _listEnd   = restored.ListEnd;
            _listTypes = restored.ListTypes ?? Enumerable.Empty<ProcessType>();
        }

        await RefreshRatesAsync();
        _ = LiveRateLoopAsync();
    }

    /// <summary>Güncel görünüm durumunu sekmeye iter (kalıcılaşır) — her görünüm değişiminde çağrılır.</summary>
    private void PushTabState()
    {
        if (CurrentMdiTab is null) return;
        TabPageState.Write(TabStateWriter, CurrentMdiTab, new CurrentTransactionTabState(
            _listMode, _listStart, _listEnd, _listTypes.ToArray(), _balanceViewMode));
    }

    private string GridStyle()
    {
        // ── MOBİL SATIRLAR NEDEN AÇIKÇA max-content (2026-08-05: Hakan "p2 ve p3 hesap panelini eziyor" dedi) ──
        // Bu grid'in kabı, MdiTabPane'in "height:100%; display:flex; flex-direction:column" kök div.inin
        // FLEX ÖĞESİDİR (Components/Mdi/MdiTabPane.razor). Kap overflow-y:auto taşıdığı için asgari boyutu 0'dır
        // ve flex-shrink ile sekme yüksekliğine KISILIR → grid KESİN bir alanla boyutlanır, boş alan NEGATİF olur.
        // Negatif boş alanda 'auto' satırlar TABAN boyutlarında donar (grid asla tabanın altına inmez, ama üstüne
        // de çıkmaz). p1'in overflow:hidden'ı otomatik asgari boyutunu 0'a indirdiğinden p1'in TABANI 0'dır →
        // satır yalnız 4+4px padding kadar çizilir ve panel görünmez olur. Blink'te ölçüldü: HEAD'de satırlar
        // "8px 308px 548px", max-content ile "508px 308px 548px".
        // max-content TABANI kısılamaz → her panel kendi içeriği kadar yer alır; toplam taşarsa kabın kendi
        // overflow-y:auto'su kaydırır (mobilde amaçlanan davranış zaten budur).
        // "Gerilme kaybolur mu?" HAYIR: kabın flex-grow'u 0 olduğu için boş alan hiçbir zaman POZİTİF olmaz —
        // kap içeriğine sarılır. Yani max-content'e geçmek hiçbir dalda görünümü değiştirmez, YALNIZ çökmeyi
        // engeller (ölçüldü). DÖRT mobil dalın hepsine konur: tek panelli dalda da içerik uzun telefonda
        // sığmayınca satır kaba kırpılıyor ve scrollHeight kabın boyuna eşit kaldığı için taşan kısım
        // ERİŞİLEMEZ oluyordu (ölçüm: 908px içerik, 700px satır, kaydırma YOK).
        // Masaüstünde gerekmez: orada kap height:100% ile kesin ve p1 satırı bilinçli minmax(0,auto) (aşağıda).
        if (!_currentSubAccountId.HasValue)
        {
            return _isMobile
                ? "display:grid; gap:0px; grid-template-columns:1fr; grid-template-rows:max-content; grid-template-areas:'p1'; overflow-y:auto; max-height:calc(100vh - 110px); max-height:calc(100dvh - 110px);"
                : "display:grid; gap:0px; height:100%; grid-template-columns:1fr; grid-template-areas:'p1';";
        }

        if (!_accountLocked)
        {
            return _isMobile
                ? "display:grid; gap:0px; grid-template-columns:1fr; grid-template-rows:max-content max-content; grid-template-areas:'p1' 'p3'; overflow-y:auto; max-height:calc(100vh - 110px); max-height:calc(100dvh - 110px);"
                : "display:grid; gap:0px; height:100%; grid-template-columns:minmax(0,1fr) 300px; grid-template-areas:'p1 p3';";
        }

        // Mobil satırlar max-content — gerekçe metodun başında (kap, MdiTabPane'in flex kök div'inde kısılıyor).
        if (_isMobile)
            return _listMode && !_processActive
                ? "display:grid; gap:0px; grid-template-columns:1fr; grid-template-rows:max-content max-content; grid-template-areas:'p3' 'p2'; overflow-y:auto; max-height:calc(100vh - 110px); max-height:calc(100dvh - 110px);"
                : "display:grid; gap:0px; grid-template-columns:1fr; grid-template-rows:max-content max-content max-content; grid-template-areas:'p1' 'p3' 'p2'; overflow-y:auto; max-height:calc(100vh - 110px); max-height:calc(100dvh - 110px);";

        // p1 satırı minmax(0,auto): içerik sığdığı sürece "auto" gibi davranır (yerleşim DEĞİŞMEZ), ama
        // süreç paneli uzun olduğunda satır sıkışabilir — böylece panel grid'i taşırıp p2'yi ezmek yerine
        // KENDİ içinde kayar ve başlık/Kaydet çubuğu sticky kalır (bkz. ProcessPanelBase max-height:100%).
        return _listMode && !_processActive
            ? "display:grid; gap:0px; height:100%; grid-template-columns:minmax(0,1fr) 300px; grid-template-areas:'p2 p3';"
            : "display:grid; gap:0px; height:100%; grid-template-columns:minmax(0,1fr) 300px; grid-template-rows:minmax(0,auto) 1fr; grid-template-areas:'p1 p3' 'p2 p3';";
    }

    private static string PanelBox() =>
        "box-sizing:border-box; border-radius:12px; padding:4px; min-width:0;";
}
