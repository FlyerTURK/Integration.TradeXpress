using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Çeşni paneli (Assay=14): takoz girişlerinde biriken numune (AssayAmount) havuzundan cariye saf metal
/// çıkışı. Yön SABİT ÇIKIŞ, parasal alan YOK (Total=0, Kodu="CESNI"). HAS/GUM = Miktar × milyem
/// (salt-okunur); açılışta mevcut çeşni stoğu ön-doldurulur (GetAssayStockAsync — legacy Cesni paritesi).
/// Metal leg birimleri (HAS/GUM) satıra panelden yazılır — AssayBalancePoster bunlara postlar.
/// </summary>
public partial class AssayProcessPanel : IVoucherLineEditPanel
{
    /// <summary>Dar ekran bayrağı — yalnız ortak kabuğa (ProcessPanelBase) geçmek için. Bu panelin KENDİ
    /// içeriği DxFormLayout ile responsive olduğundan alanlarda kullanılmaz; kabuk ise mobil kipi (yükseklik
    /// sınırı + sticky başlık/Kaydet çubuğu) bu bayrakla açar — geçilmediğinde mobilde hiç devreye girmiyordu.</summary>
    private bool _isMobile;

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

    /// <summary>Legacy Kodu paritesi — çeşni satırının sabit emtia kodu.</summary>
    private const string AssayCommodityCode = "CESNI";

    private List<CurrencyUnitListDto> _units = new();
    private Guid? _hasUnitId;
    private Guid? _gumUnitId;

    private decimal _amount;
    private decimal _auMilyem;
    private decimal _agMilyem;
    private decimal _hasValue;
    private decimal _gumValue;
    private string? _description;
    private string? _error;

    private bool _isEdit;
    private Guid  _editingLineId;

    private Task _initTask = Task.CompletedTask;

    protected override Task OnInitializedAsync()
    {
        _initTask = InitializeAsync();
        return _initTask;
    }

    private async Task InitializeAsync()
    {
        var unitResult = await CurrencyUnitService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _units     = unitResult.Items.ToList();
        _hasUnitId = UnitIdOf(CurrencyUnitCode.HAS);
        _gumUnitId = UnitIdOf(CurrencyUnitCode.GUM);

        // Açılışta mevcut çeşni stoğunu ön-doldur (legacy iFCesni: GetCesniStoklari — milyem Has/Miktar).
        if (!_isEdit)
        {
            var stock = await VoucherService.GetAssayStockAsync();
            if (stock.Amount > 0m)
            {
                _amount   = stock.Amount;
                _auMilyem = stock.AuMilyem;
                _agMilyem = stock.AgMilyem;
            }
        }

        Recalc();
    }

    private Guid? UnitIdOf(string code)
    {
        return _units.FirstOrDefault(u => string.Equals(u.Code, code, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private void OnAmountChanged(decimal v)   { _amount = v; Recalc(); }
    private void OnAuMilyemChanged(decimal v) { _auMilyem = v; Recalc(); }
    private void OnAgMilyemChanged(decimal v) { _agMilyem = v; Recalc(); }

    /// <summary>HAS/GUM salt-okunur değerleri (Miktar × milyem) — poster ile aynı formül.</summary>
    private void Recalc()
    {
        _hasValue = _amount * _auMilyem;
        _gumValue = _amount * _agMilyem;
        StateHasChanged();
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
        _error = null;
        if (_amount <= 0m)
        {
            _error = L["TradeXpress:Voucher:AmountRequired"].Value;   // Miktar ZORUNLU (server da doğrular)
            return;
        }
        if (_hasUnitId is null)
        {
            _error = L["Voucher_Op_PayUnitRequired"].Value;           // HAS birimi çözülemedi (kurulum eksik)
            return;
        }

        var dto = new VoucherLineDto
        {
            Id           = _editingLineId,
            VoucherId    = VoucherId,
            CompanyId    = CompanyId,
            BranchId     = BranchId,
            VaultId      = VaultId,
            AccountId    = AccountId,
            SubAccountId = SubAccountId,
            VoucherDate  = VoucherDate == default ? BusinessClock.Now() : VoucherDate,
            VoucherDescription = VoucherDescription,
            Type         = ProcessType.Assay,
            Direction    = ProcessDirectionType.Outbound,   // yön SABİT ÇIKIŞ

            CommodityCode = AssayCommodityCode,
            Amount        = _amount,
            Factor        = _auMilyem,        // altın milyemi
            SilverFactor  = _agMilyem,        // gümüş milyemi
            // Metal leg birimleri — poster HAS'a −(Miktar×Factor), GUM'a −(Miktar×SilverFactor) postlar.
            MainUnitId    = _hasUnitId.Value,
            SilverUnitId  = _gumUnitId,
            // Parasal alan YOK: saf metal çıkışı (Fiyat=Tutar=0, birim leg'i yok).
            Total         = 0m,
            PayTotal      = 0m,
            Description   = _description,
        };

        // Kararı persister verir (TEK yer): dış cari → normal fiş kaydı · iç kasa → Teyit teklifi ·
        // beyan kipi → alıcının kendi satırı. Teyit yollarında fiş OLUŞMAZ → result.Line null.
        try
        {
            var persisted = await Persister.PersistAsync(new VoucherLinePersistRequest(
                dto, CounterpartyVaultId, VaultId, DeclareConfirmationId));

            if (persisted.Line is not { } saved)
            {
                // Teyit kuruldu/beyan edildi ya da ön koşul sağlanmadı: fiş/grid durumu ELLENMEZ (toast persister'da).
                _description = null;
                if (persisted.Outcome != VoucherLinePersistOutcome.Blocked)
                {
                    await OnConfirmationSubmitted.InvokeAsync(persisted.Outcome);
                }
                return;
            }

            VoucherId = saved.VoucherId;
            await OnSaved.InvokeAsync(saved);
            Ui.ShowSuccessToast(_isEdit ? L["Voucher_LineUpdated"].Value : L["Voucher_LineAdded"].Value);

            if (_isEdit)
            {
                await OnBack.InvokeAsync();
                return;
            }

            // Kayıt sonrası: kalan stok yeniden okunur ve form onunla ön-doldurulur (ardışık çıkış).
            _description = null;
            var stock = await VoucherService.GetAssayStockAsync();
            _amount   = stock.Amount > 0m ? stock.Amount : 0m;
            _auMilyem = stock.Amount > 0m ? stock.AuMilyem : 0m;
            _agMilyem = stock.Amount > 0m ? stock.AgMilyem : 0m;
            Recalc();
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    /// <summary>Düzeltme: kayıtlı çeşni satırını forma yansıtır (stok ön-doldurması atlanır).</summary>
    public async Task LoadForEditAsync(VoucherLineDto dto)
    {
        _isEdit = true;
        await _initTask;

        _editingLineId = dto.Id;
        _amount        = dto.Amount;
        _auMilyem      = dto.Factor;
        _agMilyem      = dto.SilverFactor ?? 0m;
        _description   = dto.Description;
        VoucherId      = dto.VoucherId;

        Recalc();
        StateHasChanged();
    }
}
