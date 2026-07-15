using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>Karşı taraf kipi — panelin üst iki combosunun ANLAMINI değiştirir (spec §7):
/// <b>Cari</b> = bugünkü akış (Hesap→Alt Hesap; kayıt normal postlanır) · <b>İç Kasa</b> = Şube→Kasa
/// (seçilen kasa karşı taraf; kayıt POSTLANMAZ, Teyit teklifi kurulur).
/// <para>Kipi ROTA/MENÜ belirler (form içinde seçilmez): <c>/cari-islemler</c> → <see cref="CurrentAccount"/>,
/// <c>/transfers</c> → <see cref="InternalVault"/>.</para></summary>
public enum CounterpartyMode
{
    CurrentAccount,
    InternalVault,
}

public partial class AccountSelectionPanel
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Karşı taraf kipi — ROTADAN gelir (form içinde combo YOK). Kip sabittir: bir sekme
    /// ömrü boyunca kip değişmez, dolayısıyla kip-değişimi sıfırlama akışına gerek yoktur.</summary>
    [Parameter] public CounterpartyMode Mode { get; set; } = CounterpartyMode.CurrentAccount;

    [Parameter] public Guid? InitialSubAccountId { get; set; }
    [Parameter] public Guid? InitialVoucherId { get; set; }

    [Parameter] public EventCallback<Guid> OnLineSaved { get; set; }
    [Parameter] public EventCallback<SubAccountListDto?> OnSubAccountSelected { get; set; }
    [Parameter] public EventCallback<Guid?> OnVoucherOpened { get; set; }
    /// <summary>Süreç paneli (Nakit vb.) açık/kalı değişince forma bildirir.</summary>
    [Parameter] public EventCallback<bool> OnProcessActiveChanged { get; set; }
    /// <summary>Cari panel TAMAM ile kilitlenince/açılınca forma bildirir (Düzelt/Sil görünürlüğü).</summary>
    [Parameter] public EventCallback<bool> OnLockChanged { get; set; }
    /// <summary>Liste butonu → form liste modunu açar.</summary>
    [Parameter] public EventCallback OnListRequested { get; set; }

    private VoucherCreateDto _model = new();
    private bool _locked;
    private bool _showActionToolbar;
    private string? _activeProcess;
    private string? _selectedAccountCode;
    private string? _selectedSubAccountCode;

    private CashProcessPanel? _cashPanel;
    private ServiceProcessPanel? _servicePanel;
    private ConvertProcessPanel? _convertPanel;
    private FutureProcessPanel? _futurePanel;
    private ScrapProcessPanel? _scrapPanel;
    private MetalProcessPanel? _metalPanel;
    private StoneProcessPanel? _stonePanel;
    private JewelryProcessPanel? _jewelryPanel;
    private GoodProcessPanel? _goodPanel;
    private BullionProcessPanel? _bullionPanel;
    private BullionExitPanel? _bullionExitPanel;
    private AssayProcessPanel? _assayPanel;
    private DebitNoteProcessPanel? _debitNotePanel;
    private TransferProcessPanel? _transferPanel;
    private VoucherLineDto? _pendingEdit;
    private SubAccountListDto? _pendingSubAccount;


    private List<SubAccountListDto> _subAccounts = new();
    // Kendi kasa listem = "o şubede + YETKİLİ olduğum" kasalar (GetMyVaultsAsync — kapsam-grant'i sunucuda
    // eler). Genel VaultAppService.GetListAsync yönetim listesidir, kapsam daraltmaz → burada kullanılmaz.
    private List<MyVaultDto>        _branchVaults = new();

    /// <summary>Seçili alt hesabın AccountId'si (2026-07-15: ayrı Account combo YOK — tek kaynak
    /// <see cref="OnSubAccountChanged"/>; burada yalnız SubAccount edit-popup önseçimi için tutulur,
    /// bkz. <see cref="AccountPopupExtra"/>).</summary>
    private Guid? _selectedAccountId;

    // ── İç karşı taraf (Teyit) kipi — TEK combo (2026-07-16): Şube kodu, Kasa combosunda bir KOLON.
    private List<VaultListDto> _counterpartyVaults = new();
    private Guid? _counterpartyVaultId;

    /// <summary>Kip rotadan sabit gelir (<see cref="Mode"/>) — kullanıcı form içinden değiştiremez.</summary>
    private bool IsInternalMode => Mode == CounterpartyMode.InternalVault;

    /// <summary>Alt satırların (fiş/kasa/açıklama/tarih/TAMAM) görünürlük koşulu — KİPTEN BAĞIMSIZ: cari
    /// seçilmiş olmalı. İç kipte de dolar: seçilen KASA doğrudan bu alana oturur
    /// (<see cref="OnCounterpartyVaultChangedAsync"/>) → form dış cari formuyla birebir aynı sürer.</summary>
    private bool SelectionReady => _model.SubAccountId.HasValue;

    /// <summary>TAMAM butonunun aktiflik koşulu (2026-07-16 kullanıcı kararı): fiş seçimi HARİÇ, alt satırların
    /// (Kasa/Açıklama) HERHANGİ biri boşsa buton pasif kalır. Tarih dahil edilmedi — <see cref="_displayVoucherDate"/>
    /// nullable değil, her zaman varsayılan bir değerle dolu gelir (gerçek anlamda "boş" olamaz).</summary>
    private bool CanConfirm =>
        _model.VaultId.HasValue &&
        !string.IsNullOrWhiteSpace(_model.Description);

    /// <summary>Karşı kasa seçenekleri — kendi kasam (başlatan) karşı taraf OLAMAZ (sunucu da reddeder).
    /// İki kaynak hariç tutulur: formdaki seçili gönderen kasa (<see cref="_model"/>.VaultId) VE çalışma
    /// parametrelerindeki (üst menü) çalıştığım kasa (<see cref="IWorkingContextService.CurrentVaultId"/>) —
    /// ikincisi form henüz senkronize olmadan/kullanıcı "Kendi kasam" combosunu değiştirdiğinde de korunsun diye.</summary>
    private IEnumerable<VaultListDto> CounterpartyVaultOptions
        => _counterpartyVaults.Where(v => v.Id != _model.VaultId && v.Id != Working.CurrentVaultId);

    private const int VoucherPageSize = 1000;
    private static readonly Guid SentinelId = new("00000000-0000-0000-0000-000000000001");
    private List<VoucherComboItem> _voucherItems = new();
    private int   _voucherOffset;
    private bool  _voucherHasMore;
    private Guid? _selectedVoucherId;

    private DateTime _displayVoucherDate = BusinessClock.Now();
    private string   _selectedVaultDisplay = string.Empty;

    // ── İşlem tipi butonları — yetki gate'i (server-side kontrol VoucherAppService.SaveLineAsync'de tekrarlanır) ──
    private readonly Dictionary<ProcessType, bool> _grantedProcesses = new();

    // ProcessType → düzenleme paneli getter'ı. Panel @ref alanları render SONRASI dolduğundan
    // değerler alan referansı değil lambda'dır (çağrı anındaki güncel referans okunur).
    private readonly Dictionary<ProcessType, Func<IVoucherLineEditPanel?>> _editPanelByType;

    public AccountSelectionPanel()
    {
        _editPanelByType = new Dictionary<ProcessType, Func<IVoucherLineEditPanel?>>
        {
            [ProcessType.Service]   = () => _servicePanel,
            [ProcessType.Convert]   = () => _convertPanel,
            [ProcessType.Future]    = () => _futurePanel,
            [ProcessType.Scrap]     = () => _scrapPanel,
            [ProcessType.Metal]     = () => _metalPanel,
            [ProcessType.Stone]     = () => _stonePanel,
            [ProcessType.Jewelry]   = () => _jewelryPanel,
            [ProcessType.Good]      = () => _goodPanel,
            [ProcessType.Assay]     = () => _assayPanel,
            [ProcessType.DebitNote] = () => _debitNotePanel,
            [ProcessType.Transfer]  = () => _transferPanel,
        };
    }

    private record VoucherComboItem(Guid Id, string VoucherNo, string Date, string VaultDisplay, DateTime VoucherDate, string? Description, string DisplayText, int CurrentTransactionCount);

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await Working.EnsureLoadedAsync();
        await LoadTransactionPermissionsAsync();

        _model.CompanyId = Working.CurrentCompanyId ?? Guid.Empty;
        _model.BranchId  = Working.CurrentBranchId  ?? Guid.Empty;

        // SIRALI await (Task.WhenAll DEĞİL): Blazor Server'da bu servisler aynı circuit scope'unun
        // DbContext'ini paylaşır — paralel iki EF sorgusu aralıklı "second operation started" çökmesi üretir.
        var subResult   = await SubAccountService.GetListAsync(new SubAccountListRequestDto { BranchId = Working.CurrentBranchId, MaxResultCount = 1000 });
        var myVaults    = await VaultService.GetMyVaultsAsync(Working.CurrentBranchId);
        _subAccounts  = subResult.Items.ToList();
        _branchVaults = myVaults;

        // İç kip rotadan sabit → karşı KASA listesi baştan hazır olmalı (kip combo'su yok, tetikleyecek
        // "kip değişti" anı da yok). Cari kipte bu sorgu HİÇ atılmaz — dış akışın maliyeti değişmez.
        if (IsInternalMode)
        {
            try
            {
                await EnsureCounterpartyVaultsAsync();
            }
            catch (Exception ex)
            {
                // Kasa listesi okunamıyorsa iç kip kullanılamaz. Artık Cari'ye DÜŞÜLMEZ (kip rotanın
                // sözleşmesi; sessizce dış cariye kaymak kullanıcıyı yanlış deftere yazdırabilirdi) →
                // sebep gösterilir, Kasa combo'su boş kalır ve akış ilerlemez.
                Ui.ShowErrorToast(ex.Message);
            }
            return;   // iç kipte cari ön-seçimi (subAccount/voucher) anlamsız — o URL state'i dış akışın
        }

        if (InitialSubAccountId.HasValue)
        {
            await OnSubAccountChanged(InitialSubAccountId);
            await OnSubAccountLostFocus(); // Seçimi anında forma bildir (blur bekleme)

            if (InitialVoucherId.HasValue)
            {
                await OnVoucherChanged(InitialVoucherId);
                await OnConfirmClicked(); // Auto-lock and open voucher!
            }
        }
    }

    /// <summary>Her işlem tipi butonu için ayrı yetki (ProcessTypePermissionMap tek kaynak) — yetkisiz buton
    /// GÖRÜNMEZ. UI gate'tir; gerçek yetki denetimi server-side'da VoucherAppService.SaveLineAsync'de tekrarlanır.</summary>
    private async Task LoadTransactionPermissionsAsync()
    {
        foreach (var type in Enum.GetValues<ProcessType>())
        {
            _grantedProcesses[type] = await AuthorizationService.IsGrantedAsync(ProcessTypePermissionMap.PermissionFor(type));
        }
    }

    /// <summary>İşlem tipi butonunun görünürlüğü (LoadTransactionPermissionsAsync sonucu; yüklenmemişse false).</summary>
    private bool IsProcessGranted(ProcessType type)
    {
        return _grantedProcesses.TryGetValue(type, out var granted) && granted;
    }

    /// <summary>İşlem tipi butonu çizilsin mi: YALNIZ yetki. Kipe göre tip kısıtı YOKTUR (2026-07-15 kullanıcı
    /// kararı) — iç kip cari kipiyle birebir aynı işlem setini sunar; sunucu da yalnız izni arar.</summary>
    private bool IsProcessVisible(ProcessType type)
    {
        return IsProcessGranted(type);
    }

    /// <summary>Karşı KASA listesi (working şirket, TÜM şubeler) — yalnız iç kipte, panel açılışında okunur.
    /// Tek combo (2026-07-16 kullanıcı kararı — Cari işlemlerdeki Account+SubAccount sadeleştirmesiyle AYNI
    /// desen): ayrı bir Şube cascade adımı YOK, Şube kodu dropdown'da bir KOLON olarak görünür.</summary>
    private async Task EnsureCounterpartyVaultsAsync()
    {
        if (_counterpartyVaults.Count > 0)
        {
            return;
        }

        var result = await VaultService.GetListAsync(new VaultListRequestDto { MaxResultCount = 1000 });
        _counterpartyVaults = result.Items.Where(v => v.IsActive).ToList();
    }

    /// <summary>Karşı KASA seçildi = karşı taraf belirlendi (Teyit'in muhatabı).
    /// <para><b>Kasa KASADIR (2026-07-15 ürün kararı):</b> artık sahte bir cariye ÇÖZÜLMEZ — Şube→Kasa
    /// DOĞRUDAN karşı-taraf alanlarına oturur (<c>_model.AccountId</c>=Şube, <c>_model.SubAccountId</c>=Kasa;
    /// fişte <c>AccountType=Vault</c>). Alanlar polimorfik olduğu için formun geri kalanı (işlem gridi ·
    /// bakiye gridi · fiş combosu · tarih · Liste) BİREBİR dış cari akışıyla, tek satır değişmeden çalışır —
    /// hepsi SubAccountId ile sürülür.</para></summary>
    private async Task OnCounterpartyVaultChangedAsync(Guid? vaultId)
    {
        _counterpartyVaultId = vaultId;

        // Kendi kasam boşsa şubemin ilk kasasını başlatan yap (Cari akışındaki varsayılanla aynı davranış).
        if (_model.VaultId == null && _branchVaults.Count > 0)
        {
            _model.VaultId = _branchVaults[0].Id;
        }

        await ApplyCounterpartyVaultSelectionAsync(vaultId);
    }

    /// <summary>Seçilen karşı KASAYI (ya da temizlemeyi) Cari kipiyle AYNI kanaldan uygular: model + kodlar +
    /// fiş listesi + forma bildirim. Böylece iç kip için ikinci bir "seçim uygulama" yolu doğmaz.
    /// <para>Polimorfik eşleme: Şube → AccountId/AccountCode · Kasa → SubAccountId/SubAccountCode. Başlık
    /// zaten Şube/Kasa kodunu gösterir; kod artık fişe de SNAPSHOT olarak yazılır (ham GUID gösterimi bitti).</para></summary>
    private async Task ApplyCounterpartyVaultSelectionAsync(Guid? vaultId)
    {
        var vault = vaultId.HasValue ? _counterpartyVaults.FirstOrDefault(v => v.Id == vaultId.Value) : null;

        // Yeni kasa seçiminde ARADAKİ await'lerde (LoadVouchersAsync) combo zaten yeni kasayı gösterirken
        // grid/bakiye eski kasaya ait kalmasın diye (2026-07-16 kullanıcı kararı) önce null'la temizletiyoruz.
        if (vault is not null)
        {
            await OnSubAccountSelected.InvokeAsync(null);
        }

        _model.AccountType      = vault is null ? AccountType.CurrentAccount : AccountType.Vault;
        _model.AccountId        = vault?.BranchId ?? Guid.Empty;   // üst kimlik = ŞUBE (kasanın kendi şubesi)
        _model.SubAccountId     = vault?.Id;                       // alt kimlik = KASA
        _selectedAccountCode    = vault?.BranchCode;
        _selectedSubAccountCode = vault?.Code;
        _selectedVoucherId      = null;

        _locked            = false;
        _showActionToolbar = false;
        await SetActiveProcessAsync(null);

        _voucherItems  = new();
        _voucherOffset = 0;
        if (vault is not null)
        {
            await LoadVouchersAsync();
        }

        // Form seçimi Cari kipiyle aynı sözleşmeden öğrenir (sekme başlığı + bakiye + grid tek yoldan sürülür).
        await OnSubAccountSelected.InvokeAsync(vault is null
            ? null
            : new SubAccountListDto
            {
                Id          = vault.Id,
                AccountId   = vault.BranchId,
                Code        = vault.Code,
                AccountCode = vault.BranchCode,
            });
    }

    /// <summary>Cari seçimini dışarıdan temizler — form, aynı cari BAŞKA bir Cari İşlemler sekmesinde
    /// zaten açıksa seçimi geri alıp o sekmeye geçer (aynı carinin ikinci sekmesi açılmaz).</summary>
    public async Task ClearSubAccountSelectionAsync()
    {
        // İç kipte seçim ekseni kasadır → carinin yanında KASA seçimi de düşmeli (aksi halde combo dolu
        // görünürken form boş kalırdı). Cari kipinde davranış aynen korunur.
        if (IsInternalMode)
        {
            await OnCounterpartyVaultChangedAsync(null);
            return;
        }

        await OnSubAccountChanged(null);
    }

    private async Task OnSubAccountChanged(Guid? subAccountId)
    {
        _model.SubAccountId = subAccountId;
        _selectedVoucherId  = null;

        var selected = subAccountId.HasValue
            ? _subAccounts.FirstOrDefault(s => s.Id == subAccountId.Value)
            : null;
        _model.AccountId        = selected?.AccountId ?? Guid.Empty;
        _selectedAccountCode    = selected?.AccountCode;
        _selectedSubAccountCode = selected?.Code;

        // Cascade senkronu: alt hesap seçilince üst (Account) combo parent'ına oturur — kullanıcı alt
        // hesabı doğrudan (üst combo boşken) seçtiğinde de iki combo tutarlı kalır. Temizlemede dokunma
        // (üst combo filtre olarak kalabilir).
        if (selected is not null)
        {
            _selectedAccountId = selected.AccountId;
        }

        if (_model.VaultId == null && _branchVaults.Count > 0)
            _model.VaultId = _branchVaults[0].Id;

        _locked            = false;
        _showActionToolbar = false;
        await SetActiveProcessAsync(null);
        _voucherItems      = new();
        _voucherOffset    = 0;
        if (subAccountId.HasValue)
            await LoadVouchersAsync();

        // Seçim null ise (temizleme) direkt bildir; değilse blur'da GERÇEK cari bildirilecek. Ama YENİ bir
        // seçimde de ARADAKİ pencerede combo zaten yeni cariyi gösterirken grid/bakiye eski cariye ait
        // kalmasın diye (2026-07-16 kullanıcı kararı) önce null'la temizletiyoruz — karışıklığı giderir.
        if (selected == null)
        {
            _pendingSubAccount = null;
            await OnSubAccountSelected.InvokeAsync(null);
        }
        else
        {
            _pendingSubAccount = selected;
            await OnSubAccountSelected.InvokeAsync(null);
        }
    }

    private async Task OnSubAccountLostFocus()
    {
        if (_pendingSubAccount != null)
        {
            var sa = _pendingSubAccount;
            _pendingSubAccount = null;
            await OnSubAccountSelected.InvokeAsync(sa);
        }
    }

    private async Task LoadVouchersAsync()
    {
        var result = await VoucherService.GetListAsync(new VoucherListRequestDto
        {
            SubAccountId   = _model.SubAccountId,
            SkipCount      = _voucherOffset,
            MaxResultCount = VoucherPageSize,
        });

        _voucherItems.RemoveAll(x => x.Id == SentinelId);

        foreach (var v in result.Items)
        {
            var dateStr = v.VoucherDate.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            var displayText = L["VoucherComboDisplayTextFormat", v.VoucherNumber, dateStr, v.LineCount].Value;
            _voucherItems.Add(new VoucherComboItem(
                v.Id,
                $"#{v.VoucherNumber}",
                dateStr,
                v.VaultDisplay,
                v.VoucherDate,
                v.Description,
                displayText,
                v.LineCount));
        }

        _voucherOffset += result.Items.Count;
        _voucherHasMore = _voucherOffset < result.TotalCount;

        if (_voucherHasMore)
            _voucherItems.Add(new VoucherComboItem(SentinelId, "...", $"{result.TotalCount - _voucherOffset} more...", string.Empty, default, null, "...", 0));
    }

    private async Task OnVoucherChanged(Guid? id)
    {
        if (id == SentinelId) { await LoadVouchersAsync(); return; }

        _selectedVoucherId = id;

        if (id.HasValue)
        {
            var item = _voucherItems.FirstOrDefault(x => x.Id == id.Value);
            if (item is not null)
            {
                _displayVoucherDate   = item.VoucherDate;
                _selectedVaultDisplay = item.VaultDisplay;
                _model.Description    = item.Description;
            }
        }
        else
        {
            _displayVoucherDate   = BusinessClock.Now();
            _model.VoucherDate    = _displayVoucherDate;
            _selectedVaultDisplay = string.Empty;
            _model.Description    = null;
        }
    }

    private void OnVoucherDateChanged(DateTime d)
    {
        _displayVoucherDate = d;
        if (!_selectedVoucherId.HasValue)
            _model.VoucherDate = d;
    }

    private async Task OnConfirmClicked()
    {
        _locked            = !_locked;
        _showActionToolbar = _locked;
        await OnLockChanged.InvokeAsync(_locked);
        if (!_locked)
        {
            await SetActiveProcessAsync(null);
            await OnVoucherOpened.InvokeAsync(null);   // HESAP SEÇ → gridi temizle
        }
        else
        {
            // TAMAM → seçili fişin (varsa) hareketlerini forma yüklet; yeni fiş ise null (boş grid).
            await OnVoucherOpened.InvokeAsync(_selectedVoucherId);
        }
    }

    private async Task SetActiveProcessAsync(string? process)
    {
        _activeProcess = process;
        await OnProcessActiveChanged.InvokeAsync(process != null);
    }

    private Task OnCashClicked()    => SetActiveProcessAsync("Cash");
    private Task OnServiceClicked() => SetActiveProcessAsync("Service");
    private Task OnConvertClicked() => SetActiveProcessAsync("Convert");
    private Task OnFutureClicked()  => SetActiveProcessAsync("Future");
    private Task OnScrapClicked()   => SetActiveProcessAsync("Scrap");
    private Task OnMetalClicked()   => SetActiveProcessAsync("Metal");
    private Task OnStoneClicked()   => SetActiveProcessAsync("Stone");
    private Task OnJewelryClicked() => SetActiveProcessAsync("Jewelry");
    private Task OnGoodClicked()    => SetActiveProcessAsync("Good");
    private Task OnBullionInClicked()  => SetActiveProcessAsync("Bullion");
    private Task OnBullionOutClicked() => SetActiveProcessAsync("BullionOut");
    private Task OnAssayClicked()      => SetActiveProcessAsync("Assay");
    private Task OnDebitNoteClicked()  => SetActiveProcessAsync("DebitNote");
    private Task OnTransferClicked()   => SetActiveProcessAsync("Transfer");

    private bool _accountPopupSaved;

    /// <summary>Cari combo "düzelt": seçili ALT-HESABI (SubAccount) POPUP'ta açar (standart popup+refresh+odak).
    /// SubAccount formunda Account lookup yer alır (yeni kayıtta seçilir, edit'te salt-okunur).</summary>
    private async Task OnEditSubAccountAsync(Guid? subAccountId)
    {
        if (subAccountId is not { } id || id == Guid.Empty) return;
        var sub = _subAccounts.FirstOrDefault(s => s.Id == id);
        var title = sub is not null ? $"{L["SubAccount"]}: {sub.AccountSubCodeDisplay}" : L["SubAccount"].Value;
        await OpenSubAccountPopupAsync(id, title);
    }

    /// <summary>Cari combo "ekle": YENİ alt-hesabı (SubAccount) POPUP'ta açar (standart popup+refresh+odak).</summary>
    private async Task<Guid?> OnAddSubAccountAsync()
    {
        await OpenSubAccountPopupAsync(null, L["SubAccount"].Value);
        return null;   // yeni id popup akışında oluşur; seçim aşağıda (refresh sonrası) yapılır
    }

    /// <summary>STANDART davranış: SubAccount edit POPUP'ı (framework merkezî yolu IViewOpener→IPopupService) →
    /// kaydedilince combo listesini TAZELE + ilgili kayda ODAKLAN (ekle → yeni eklenen; düzelt → mevcut).
    /// İptalde (kaydetmeden kapat) hiçbir şey yapılmaz.</summary>
    private async Task OpenSubAccountPopupAsync(Guid? subAccountId, string title)
    {
        _accountPopupSaved = false;
        var beforeIds = _subAccounts.Select(s => s.Id).ToHashSet();

        await ViewOpener.OpenAsync(
            typeof(Integration.TradeXpress.Blazor.Client.Pages.Accounts.SubAccountEditHost),
            subAccountId, title, TradeXpressIcons.SubAccount, AccountPopupExtra());

        if (!_accountPopupSaved) return;                       // iptal → tazeleme/odak yok

        await ReloadSubAccountsAsync();                        // liste refresh

        // Odaklan: ekle → yeni eklenen alt-hesap (before'da olmayan); düzelt → mevcut seçili kayıt (display tazelenir).
        var focus = _subAccounts.FirstOrDefault(s => !beforeIds.Contains(s.Id))
                    ?? _subAccounts.FirstOrDefault(s => s.Id == _model.SubAccountId);
        if (focus is not null)
            await OnSubAccountChanged(focus.Id);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Popup kancaları: kaydet → bayrak set + kapat; kapat → sadece kapat (merkezî IPopupService).
    /// Üst comboda hesap seçiliyse yeni alt hesaba ÖNSEÇİLİ gider (formda Account lookup hazır gelir).</summary>
    private Dictionary<string, object> AccountPopupExtra()
    {
        var extra = new Dictionary<string, object>
        {
            { "OnSaved",  EventCallback.Factory.Create(this, () => { _accountPopupSaved = true; PopupService.Close(); }) },
            { "OnClosed", EventCallback.Factory.Create(this, () => PopupService.Close()) },
        };
        if (_selectedAccountId is { } acc)
        {
            extra["AccountId"] = (Guid?)acc;   // SubAccountEditHost.AccountId → ApplyNewDefaults önseçimi
        }
        return extra;
    }

    private async Task ReloadSubAccountsAsync()
    {
        var res = await SubAccountService.GetListAsync(new SubAccountListRequestDto { BranchId = Working.CurrentBranchId, MaxResultCount = 1000 });
        _subAccounts = res.Items.ToList();
    }
    /// <summary>Süreç paneline geçen fiş bağlamı — 10 ayrı parametre yerine tek nesne
    /// (VoucherLineContext). ProcessPanelHostBase tabanlı 6 panel (Cash/Metal/Scrap/Future/Convert/Service)
    /// bunu kullanır; kalanlar (Stone/Jewelry/Bullion/Assay/DebitNote/Transfer) kademeli geçecek.</summary>
    private VoucherLineContext BuildLineContext() => new()
    {
        CompanyId          = _model.CompanyId,
        BranchId           = _model.BranchId,
        VaultId            = _model.VaultId,
        AccountId          = _model.AccountId,
        SubAccountId       = _model.SubAccountId,
        VoucherDate        = _model.VoucherDate,
        VoucherDescription = _model.Description,
        VoucherId          = _selectedVoucherId,
        AccountCode        = _selectedAccountCode,
        SubAccountCode     = _selectedSubAccountCode,
        // Yalnız iç kipte dolu: panel kaydı postlamaz, Teyit teklifi kurar. Cari kipinde null → akış değişmez.
        CounterpartyVaultId = IsInternalMode ? _counterpartyVaultId : null,
    };

    private Task OnProcessBack()    => SetActiveProcessAsync(null);
    private Task OnListClicked()    => OnListRequested.InvokeAsync();

    /// <summary>Form'dan çağrılır (Düzelt): satırın türüne göre ilgili paneli açıp yükler.</summary>
    public async Task BeginEditLineAsync(VoucherLineDto dto)
    {
        _selectedVoucherId = dto.VoucherId;
        _pendingEdit       = dto;            // panel render olunca yüklenecek (OnAfterRender)
        var process = dto.Type switch
        {
            ProcessType.Service => "Service",
            ProcessType.Convert => "Convert",
            ProcessType.Future  => "Future",
            ProcessType.Scrap   => "Scrap",
            ProcessType.Metal   => "Metal",
            ProcessType.Stone   => "Stone",
            ProcessType.Jewelry => "Jewelry",
            ProcessType.Good    => "Good",
            // Takoz: yön'e göre giriş (Inbound) veya çıkış (Outbound) paneli.
            ProcessType.Bullion => dto.Direction == ProcessDirectionType.Outbound ? "BullionOut" : "Bullion",
            ProcessType.Assay     => "Assay",
            ProcessType.DebitNote => "DebitNote",
            ProcessType.Transfer  => "Transfer",
            _ => "Cash",
        };
        await SetActiveProcessAsync(process);
        StateHasChanged();
    }

    /// <summary>Satırın tipine (Bullion'da yönüne) göre yüklenecek düzenleme panelini döndürür.
    /// Sözlükte olmayan tipler (Cash dahil) nakit paneline düşer; panel henüz render olmadıysa null.</summary>
    private IVoucherLineEditPanel? ResolveEditPanel(VoucherLineDto dto)
    {
        if (dto.Type == ProcessType.Bullion)
        {
            // Takoz: yön'e göre giriş (Inbound) veya çıkış (Outbound) paneli.
            if (dto.Direction == ProcessDirectionType.Outbound)
            {
                return _bullionExitPanel;
            }
            return _bullionPanel;
        }

        if (_editPanelByType.TryGetValue(dto.Type, out var getPanel))
        {
            return getPanel();
        }
        return _cashPanel;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (_pendingEdit is not { } dto)
        {
            return;
        }

        // Panel henüz render olmadıysa _pendingEdit tüketilmez → sonraki render'da tekrar denenir.
        if (ResolveEditPanel(dto) is { } panel)
        {
            _pendingEdit = null;
            await panel.LoadForEditAsync(dto);
        }

        // Panel yüklendiyse (_pendingEdit tüketildi) görünüme kaydır + ilk input'a odaklan —
        // özellikle mobilde Düzelt'e basınca panel ekran dışında kalıyordu (kullanıcı isteği).
        if (_pendingEdit is null)
        {
            try { await JS.InvokeVoidAsync("erpUx.scrollFocusPanel", "tx-active-process-panel"); } catch { }
        }
    }

    private async Task OnLineSavedInternal(VoucherLineDto result)
    {
        var vid = result.VoucherId ?? Guid.Empty;

        // Yeni fiş mi oluştu (combo'da yok mu)?
        var isNew = _voucherItems.All(x => x.Id != vid);

        // İlk satır fişi oluşturdu → sonraki satırlar aynı fişe gitsin.
        _selectedVoucherId = vid;

        if (isNew)
        {
            // Fiş listesini tazele → yeni fiş numarasıyla combo'ya gelsin ve seçili görünsün.
            _voucherItems  = new();
            _voucherOffset = 0;
            await LoadVouchersAsync();

            var item = _voucherItems.FirstOrDefault(x => x.Id == vid);
            if (item is not null)
            {
                _displayVoucherDate   = item.VoucherDate;
                _selectedVaultDisplay = item.VaultDisplay;
            }
        }

        await OnLineSaved.InvokeAsync(vid);
    }

    private async Task OnDeleteVoucherClick(MouseEventArgs _)
    {
        if (!_selectedVoucherId.HasValue) return;

        if (await Ui.ConfirmDeleteAsync(L["DeleteConfirmationMessage"].Value) != Integration.Framework.Blazor.Client.Services.Base.ConfirmDialogResult.Yes)
            return;

        try
        {
            await VoucherService.DeleteAsync(_selectedVoucherId.Value);

            _selectedVoucherId = null;
            _displayVoucherDate = BusinessClock.Now();
            _model.VoucherDate = _displayVoucherDate;
            _selectedVaultDisplay = string.Empty;
            _model.Description = null;

            _voucherItems = new();
            _voucherOffset = 0;
            await LoadVouchersAsync();

            await OnVoucherOpened.InvokeAsync(null);

            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}
