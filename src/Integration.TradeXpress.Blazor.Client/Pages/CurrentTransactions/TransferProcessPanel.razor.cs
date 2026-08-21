using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Virman paneli (Transfer=11): çift leg — bu satır kaydedilince sunucu karşı hesabın KENDİ fişinde
/// zıt yönlü ikiz satırı (aynı LinkId, aynı tutar/birim) açar/günceller. Yön etiketi ALACAK/BORÇ —
/// DB'de Inbound/Outbound saklanır (legacy quirk paritesi). Karşı hesap lookup'ı kendi hesabını ve
/// pasifleri dışlar; Miktar alanı YOK (0 gider — tip bazlı muafiyet). Açıklama sunucuda legacy
/// "{kaynak}/{karşı}:{desc}" formatına çevrilir; panel ham metni gönderir.
/// </summary>
public partial class TransferProcessPanel : IVoucherLineEditPanel
{
    [Parameter] public EventCallback OnBack { get; set; }
    [Parameter] public string? AccountCode { get; set; }
    [Parameter] public string? SubAccountCode { get; set; }

    [Parameter] public Guid CompanyId { get; set; }
    [Parameter] public Guid BranchId { get; set; }
    [Parameter] public Guid? VaultId { get; set; }
    [Parameter] public Guid AccountId { get; set; }
    [Parameter] public Guid? SubAccountId { get; set; }
    [Parameter] public DateTime VoucherDate { get; set; } = BusinessClock.Now();
    [Parameter] public string? VoucherDescription { get; set; }
    [Parameter] public Guid? VoucherId { get; set; }
    [Parameter] public EventCallback<VoucherLineDto> OnSaved { get; set; }

    /// <summary>İÇ KARŞI TARAF (Teyit) kipi: doluysa satır POSTLANMAZ — Teyit teklifi kurulur.
    /// Null = normal cari akışı (davranış birebir aynı).</summary>
    [Parameter] public Guid? CounterpartyVaultId { get; set; }

    /// <summary>BEYAN kipi (gelen kutusundan "Kendi Girişimi Yaz").</summary>
    [Parameter] public Guid? DeclareConfirmationId { get; set; }

    /// <summary>Teyit yoluna gidildiğinde tetiklenir (fiş oluşmadığı için <see cref="OnSaved"/> tetiklenmez).</summary>
    [Parameter] public EventCallback<VoucherLinePersistOutcome> OnConfirmationSubmitted { get; set; }

    [Inject] private VoucherLinePersister Persister { get; set; } = default!;

    private bool _isMobile;

    private VoucherLineDto _model = NewModel();

    private List<CurrencyUnitListDto> _activeUnits = new();
    private List<SubAccountListDto>   _counterAccounts = new();

    private sealed record DirectionItem(ProcessDirectionType Value, string Label);
    private List<DirectionItem> _directionItems = new();

    private static VoucherLineDto NewModel()
    {
        return new VoucherLineDto
        {
            Type      = ProcessType.Transfer,
            Direction = ProcessDirectionType.Inbound,   // ALACAK varsayılan (etiket; DB'de Giriş)
        };
    }

    protected override async Task OnInitializedAsync()
    {
        // Yön combo — legacy quirk: görsel etiket ALACAK/BORÇ, DB'de GİRİŞ/ÇIKIŞ saklanır.
        _directionItems = new()
        {
            new(ProcessDirectionType.Inbound,  L["Enum:ProcessDirectionType:Credit"].Value),
            new(ProcessDirectionType.Outbound, L["Enum:ProcessDirectionType:Debit"].Value),
        };

        // SIRALI await (aynı circuit scope'unun DbContext'i — paralel EF sorgusu çöker).
        var unitResult = await CurrencyUnitService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _activeUnits = unitResult.Items.Where(u => u.IsActive).ToList();

        await ReloadCounterAccountsAsync();

        if (_model.PayUnitId is null && _activeUnits.Count > 0)
        {
            OnUnitChanged(_activeUnits[0].Id);
        }
    }

    /// <summary>Karşı hesap datasource: şirketin TÜM alt hesapları (şube kısıtsız) — kendi hesabı hariç,
    /// yalnız aktifler (legacy: aynı-hesap + pasif hariç filtre datasource'ta).</summary>
    private async Task ReloadCounterAccountsAsync()
    {
        var subResult = await SubAccountService.GetListAsync(new SubAccountListRequestDto { MaxResultCount = 1000 });
        _counterAccounts = subResult.Items
            .Where(s => s.IsActive && s.Id != SubAccountId)
            .ToList();
    }

    private bool _counterPopupSaved;

    /// <summary>Karşı hesap combo "düzelt": seçili alt-hesabı SubAccountEditHost POPUP'ında açar
    /// (standart popup+refresh+odak deseni — AccountSelectionPanel ile aynı).</summary>
    private async Task OnEditCounterAccountAsync(Guid? subAccountId)
    {
        if (subAccountId is not { } id || id == Guid.Empty) return;
        var sub = _counterAccounts.FirstOrDefault(s => s.Id == id);
        var title = sub is not null ? $"{L["SubAccount"]}: {sub.AccountSubCodeDisplay}" : L["SubAccount"].Value;
        await OpenCounterPopupAsync(id, title);
    }

    /// <summary>Karşı hesap combo "ekle": yeni alt-hesabı POPUP'ta açar (standart popup+refresh+odak).</summary>
    private async Task<Guid?> OnAddCounterAccountAsync()
    {
        await OpenCounterPopupAsync(null, L["SubAccount"].Value);
        return null;   // yeni id popup akışında oluşur; seçim aşağıda (refresh sonrası) yapılır
    }

    /// <summary>STANDART davranış: SubAccount edit POPUP'ı (merkezî IViewOpener→IPopupService) →
    /// kaydedilince combo listesini TAZELE + ilgili kayda ODAKLAN (ekle → yeni eklenen; düzelt → mevcut).
    /// İptalde hiçbir şey yapılmaz.</summary>
    private async Task OpenCounterPopupAsync(Guid? subAccountId, string title)
    {
        _counterPopupSaved = false;
        var beforeIds = _counterAccounts.Select(s => s.Id).ToHashSet();

        await ViewOpener.OpenAsync(
            typeof(Integration.TradeXpress.Blazor.Client.Pages.Accounts.SubAccountEditHost),
            subAccountId, title, TradeXpressIcons.SubAccount, CounterPopupExtra());

        if (!_counterPopupSaved) return;                       // iptal → tazeleme/odak yok

        await ReloadCounterAccountsAsync();

        // Odaklan: ekle → yeni eklenen alt-hesap (before'da olmayan); düzelt → mevcut seçim (display tazelenir).
        var focus = _counterAccounts.FirstOrDefault(s => !beforeIds.Contains(s.Id))
                    ?? _counterAccounts.FirstOrDefault(s => s.Id == _model.CounterAccountId);
        if (focus is not null)
        {
            _model.CounterAccountId = focus.Id;
        }
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Popup EventCallback'leri: kaydet → bayrak set + kapat; kapat → sadece kapat (merkezî IPopupService).</summary>
    private Dictionary<string, object> CounterPopupExtra()
    {
        return new()
        {
            { "OnSaved",  EventCallback.Factory.Create(this, () => { _counterPopupSaved = true; PopupService.Close(); }) },
            { "OnClosed", EventCallback.Factory.Create(this, () => PopupService.Close()) },
        };
    }

    private void OnAmountChanged(decimal value)
    {
        // Tutar = karşılık leg'i (PayTotal); CashBalancePoster ailesiyle aynı alan.
        _model.PayFactor = value;
        _model.PayTotal  = value;
    }

    private void OnUnitChanged(Guid? id)
    {
        var unit = id.HasValue ? _activeUnits.FirstOrDefault(u => u.Id == id.Value) : null;
        _model.PayUnitId        = id;
        _model.PayCommodityId   = id;
        _model.PayCommodityCode = unit?.Code;
    }

    private string GroupStyle()
    {
        return "display:flex; flex-direction:column; gap:4px; " + (_isMobile ? "width:100%;" : "width:120px; flex-shrink:0;");
    }

    private string ControlStyle()
    {
        return _isMobile ? "width:100%;" : "width:120px;";
    }

    private string CounterGroupStyle()
    {
        // Karşı hesap combo'su kod+ad gösterdiğinden geniş tutulur (LookupComboBox içte w-100 →
        // genişliği bu CounterGroupStyle belirler; ekle/düzelt editor butonları için ekstra pay).
        return "display:flex; flex-direction:column; gap:4px; " + (_isMobile ? "width:100%;" : "width:280px; flex-shrink:0;");
    }

    /// <summary>Kaydetme sürüyor mu — re-entrancy bayrağı (çift tıklama/Enter çift-gönderim koruması).</summary>
    private bool _saving;

    private async Task HandleSave()
    {
        if (_saving) return; // kaydetme zaten sürüyor — çift tıklamayı yut
        _saving = true;
        StateHasChanged(); // Kaydet butonu ilk await'te disabled çizilsin
        try { await HandleSaveCoreAsync(); }
        finally { _saving = false; }
    }

    private async Task HandleSaveCoreAsync()
    {
        if (_model.CounterAccountId is null || _model.PayUnitId is null || _model.PayTotal == 0m)
        {
            return; // karşı hesap/birim seçili değil ya da tutar girilmemiş
        }

        _model.VoucherId          = VoucherId;
        _model.CompanyId          = CompanyId;
        _model.BranchId           = BranchId;
        _model.VaultId            = VaultId;
        _model.AccountId          = AccountId;
        _model.SubAccountId       = SubAccountId;
        _model.VoucherDate        = VoucherDate;
        _model.VoucherDescription = VoucherDescription;
        _model.Type               = ProcessType.Transfer;
        _model.PaymentType        = ProcessPaymentType.Normal;   // kısaltma kodu VGN/VCN'in "N"i
        // Ana leg boş: Miktar alanı YOK (legacy 0 gider), parasal etki pay-leg'de.
        _model.MainUnitId = Guid.Empty;
        _model.Quantity   = 0m;
        _model.Amount     = 0m;
        _model.Factor     = 0m;
        _model.Total      = 0m;
        _model.Profit     = 0m;

        var wasEdit = _model.Id != Guid.Empty;   // save Id'yi dolduracağı için ÖNCE yakala

        // Kararı persister verir (TEK yer): dış cari → normal fiş kaydı · iç kasa → Teyit teklifi ·
        // beyan kipi → alıcının kendi satırı. Teyit yollarında fiş OLUŞMAZ → result.Line null.
        VoucherLinePersistResult persisted;
        try
        {
            persisted = await Persister.PersistAsync(new VoucherLinePersistRequest(
                _model, CounterpartyVaultId, VaultId, DeclareConfirmationId));
        }
        catch (Exception ex)
        {
            Ui.ShowErrorToast(L["Voucher_LineSaveFailed", ex.Message].Value);
            return;
        }

        if (persisted.Line is not { } result)
        {
            // Teyit kuruldu/beyan edildi ya da ön koşul sağlanmadı: fiş/grid durumu ELLENMEZ (toast persister'da).
            if (persisted.Outcome != VoucherLinePersistOutcome.Blocked)
            {
                _model.PayFactor        = 0m;
                _model.PayTotal         = 0m;
                _model.Description      = null;
                _model.CounterAccountId = null;
                _model.LinkId           = null;
                await OnConfirmationSubmitted.InvokeAsync(persisted.Outcome);
            }
            return;
        }

        VoucherId        = result.VoucherId;
        _model.VoucherId = result.VoucherId;
        _model.Id        = Guid.Empty;
        await OnSaved.InvokeAsync(result);
        Ui.ShowSuccessToast(wasEdit ? L["Voucher_LineUpdated"].Value : L["Voucher_LineAdded"].Value);

        if (wasEdit)
        {
            await OnBack.InvokeAsync();
            return;
        }

        _model.PayFactor        = 0m;
        _model.PayTotal         = 0m;
        _model.Description      = null;
        _model.CounterAccountId = null;
        _model.LinkId           = null;   // sonraki satır YENİ virman çifti (sunucu yeni LinkId üretir)
    }

    /// <summary>Düzeltme: GetDto'yu model olarak alır (combo/birim/karşı hesap seçimleri dto alanlarına bağlı).</summary>
    public Task LoadForEditAsync(VoucherLineDto dto)
    {
        _model    = dto;
        VoucherId = dto.VoucherId;
        StateHasChanged();
        return Task.CompletedTask;
    }
}
