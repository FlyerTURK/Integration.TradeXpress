using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Bullions;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

public partial class BullionProcessPanel : IVoucherLineEditPanel
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

    // ── Lookup ──
    private List<TypeOpt>  _bullionTypes  = new();
    private List<BoolOpt>  _reportOptions = new();
    private List<BoolOpt>  _extraOptions  = new();
    private List<DispOpt>  _dispositions  = new();
    private List<ModeOpt>  _laborModes    = new();
    private List<AssayOpt> _assayOffices  = new();
    private List<UnitOpt>  _allUnitOptions  = new();
    private List<UnitOpt>  _cashUnitOptions = new();
    private Dictionary<Guid, decimal> _buyByUnit = new();   // birim Id → alış kuru (kayıt anı snapshot)

    // ── Girdi ──
    // Düzeltilen satırın Id'si (Düzelt akışında LoadForEditAsync doldurur). Boş → yeni ekleme; dolu → UPDATE.
    // Diğer process panelleri bunu _model.Id üzerinde tutar; bu panel alanları ayrık taşıdığından ayrı alan.
    private Guid             _editingLineId;
    private string?          _bullionCode;
    private BullionType      _bullionType  = BullionType.Gold;
    private Guid?            _assayOfficeId;
    private string?          _reportNo;
    private bool             _isReport;
    private bool             _isExtra;
    private decimal          _amount;
    private decimal          _assayAmount;
    private decimal          _auMilyem;
    private decimal          _agMilyem;
    private decimal          _ptMilyem;
    private decimal          _pdMilyem;
    private MetalDisposition _silverMode    = MetalDisposition.Deliver;
    private MetalDisposition _platinumMode  = MetalDisposition.Deliver;
    private MetalDisposition _palladiumMode = MetalDisposition.Deliver;
    private decimal          _goldLaborRate;
    private Guid?            _goldLaborUnitId;
    private decimal          _silverLaborRate;
    private Guid?            _silverLaborUnitId;
    private decimal          _ptLaborRate;          // ERPPROV3'te YOK — eklendi (PT işçilik)
    private Guid?            _ptLaborUnitId;
    private decimal          _pdLaborRate;          // ERPPROV3'te YOK — eklendi (PD işçilik)
    private Guid?            _pdLaborUnitId;
    private BullionLaborMode _laborMode = BullionLaborMode.DeductFromGold;
    private Guid?            _payUnitId;
    private string?          _description;
    private string?          _error;

    // ── Önizleme (self-contained metal-has) ──
    private decimal _hasValue, _gumValue, _pltValue, _pldValue;
    private List<SummaryLeg> _summaryLegs = new();

    private bool _reportToggleEnabled => !string.IsNullOrWhiteSpace(_reportNo);

    private Task _initTask = Task.CompletedTask;
    protected override Task OnInitializedAsync() => _initTask = InitializeAsync();

    private async Task InitializeAsync()
    {
        _bullionTypes = new()
        {
            new(BullionType.Gold,      L["Bullion_Type_Gold"].Value),
            new(BullionType.Silver,    L["Bullion_Type_Silver"].Value),
            new(BullionType.Platinum,  L["Bullion_Type_Platinum"].Value),
            new(BullionType.Palladium, L["Bullion_Type_Palladium"].Value),
        };
        _reportOptions = new() { new(false, L["Voucher_Report_None"].Value), new(true, L["Voucher_Report_With"].Value) };
        _extraOptions  = new() { new(false, L["Voucher_Extra_Normal"].Value), new(true, L["Voucher_Extra_Extra"].Value) };
        _dispositions  = new()
        {
            new(MetalDisposition.Deliver,         L["Voucher_Disp_Deliver"].Value),
            new(MetalDisposition.ConvertToGold,   L["Voucher_Disp_ConvertToGold"].Value),
            new(MetalDisposition.DeductFromLabor, L["Voucher_Disp_DeductFromLabor"].Value),
            new(MetalDisposition.Keep,            L["Voucher_Disp_Keep"].Value),
        };
        _laborModes = new()
        {
            new(BullionLaborMode.DeductFromGold, L["Voucher_Labor_DeductFromGold"].Value),
            new(BullionLaborMode.WithCash,       L["Voucher_Labor_WithCash"].Value),
        };

        var unitResult = await CurrencyUnitService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _allUnitOptions  = unitResult.Items.Select(u => new UnitOpt(u.Id, u.Code)).ToList();
        _cashUnitOptions = _allUnitOptions;   // TODO: yalnız nakit birimleri (Type=Cash) — birim tip alanı gelince filtrele

        _assayOffices = (await AssayOfficeService.GetPickerListAsync())
            .Select(a => new AssayOpt(a.Id, a.Name)).ToList();

        var prices = await PriceService.GetCurrentPricesAsync();
        _buyByUnit = prices.ToDictionary(p => p.Id, p => p.Buy);
        _goldLaborUnitId   ??= UnitIdOf(CurrencyUnitCode.HAS);
        _silverLaborUnitId ??= UnitIdOf(CurrencyUnitCode.HAS);
        _ptLaborUnitId     ??= UnitIdOf(CurrencyUnitCode.HAS);
        _pdLaborUnitId     ??= UnitIdOf(CurrencyUnitCode.HAS);
        _payUnitId         ??= UnitIdOf(CurrencyUnitCode.TRY);
        Recalc();
    }

    private Guid? UnitIdOf(string code)
        => _allUnitOptions.FirstOrDefault(u => string.Equals(u.Code, code, StringComparison.OrdinalIgnoreCase))?.Id;

    private decimal BuyOf(Guid? id) => id is { } u ? _buyByUnit.GetValueOrDefault(u) : 0m;

    // ── Event'ler ──
    private void OnAmountChanged(decimal v)      { _amount = v; Recalc(); }
    private void OnAssayAmountChanged(decimal v) { _assayAmount = v; Recalc(); }
    private void OnAuMilyemChanged(decimal v)    { _auMilyem = v; Recalc(); }
    private void OnAgMilyemChanged(decimal v)    { _agMilyem = v; Recalc(); }
    private void OnPtMilyemChanged(decimal v)    { _ptMilyem = v; Recalc(); }
    private void OnPdMilyemChanged(decimal v)    { _pdMilyem = v; Recalc(); }
    private void OnGoldLaborRateChanged(decimal v)   { _goldLaborRate = v; Recalc(); }
    private void OnSilverLaborRateChanged(decimal v) { _silverLaborRate = v; Recalc(); }
    private void OnPtLaborRateChanged(decimal v)     { _ptLaborRate = v; Recalc(); }
    private void OnPdLaborRateChanged(decimal v)     { _pdLaborRate = v; Recalc(); }

    private void OnReportNoChanged()
    {
        if (string.IsNullOrWhiteSpace(_reportNo) && _isReport) _isReport = false;
        Recalc();
    }
    private void OnReportChanged(bool v)
    {
        _isReport = v;
        if (!_isReport) _isExtra = false;
        Recalc();
    }
    private void OnLaborModeChanged(BullionLaborMode v)
    {
        _laborMode = v;
        if (_laborMode == BullionLaborMode.WithCash && _payUnitId is null)
            _payUnitId = UnitIdOf(CurrencyUnitCode.TRY);
        Recalc();
    }

    // Önizleme — self-contained metal-has (sağ özet kutusu). Otorite = sunucu (BullionBalancePoster).
    // RAPORSUZ (varsayılan durum — _isReport başlangıçta false): milyem yok → HAS/GUM/PLT/PLD hesaplanamaz;
    // ham miktar TAKOZ pseudo-birimde (BullionConsts.PseudoUnitCode) gösterilir (ERPPROV3 UnreportedTotal paritesi).
    // Bu satır eklenmeden özet kutusu raporsuz girişte hep boş kalıyordu (grid hiç görünmüyordu).
    private void Recalc()
    {
        var qty = _amount + _assayAmount;
        var auMilyem = _isReport ? _auMilyem : 0m;
        var agMilyem = _isReport ? _agMilyem : 0m;
        var ptMilyem = _isReport && _isExtra ? _ptMilyem : 0m;
        var pdMilyem = _isReport && _isExtra ? _pdMilyem : 0m;
        _hasValue = qty * auMilyem;
        _gumValue = qty * agMilyem;
        _pltValue = qty * ptMilyem;
        _pldValue = qty * pdMilyem;

        _summaryLegs = new();
        if (_isReport)
        {
            AddLeg(CurrencyUnitCode.HAS, _hasValue);
            AddLeg(CurrencyUnitCode.GUM, _gumValue);
            AddLeg(CurrencyUnitCode.PLT, _pltValue);
            AddLeg(CurrencyUnitCode.PLD, _pldValue);
        }
        else
        {
            AddLeg(BullionConsts.PseudoUnitCode, qty);
        }
        StateHasChanged();

        void AddLeg(string code, decimal value)
        {
            if (value == 0m) return;
            _summaryLegs.Add(new SummaryLeg(code, value));
        }
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
        if (_assayOfficeId is null) { _error = L["Voucher_Op_AssayOffice"].Value; return; }
        if (_amount <= 0m)          { _error = L["Voucher_Op_Quantity"].Value; return; }

        // Ana birim: RAPORLU → takoz türünün kanonik birimi (HAS/GUM/PLT/PLD); RAPORSUZ → TAKOZ pseudo-birimi
        // (ham gram, legacy BirimId=-1). İşçilik tahsil birimi: Altından Düş → HAS, Para İle → seçilen.
        var mainUnitId = _isReport
            ? (UnitIdOf(_bullionType.MainUnitCode()) ?? UnitIdOf(CurrencyUnitCode.HAS))
            : (Guid?)BullionConsts.PseudoUnitId;
        var payUnitId  = _laborMode == BullionLaborMode.DeductFromGold ? UnitIdOf(CurrencyUnitCode.HAS) : _payUnitId;

        var dto = new VoucherLineDto
        {
            Id           = _editingLineId,   // dolu → server UpdateLine (gerçek düzeltme); boş → yeni satır
            VoucherId    = VoucherId,
            CompanyId    = CompanyId,
            BranchId     = BranchId,
            VaultId      = VaultId,
            AccountId    = AccountId,
            SubAccountId = SubAccountId,
            VoucherDate  = VoucherDate == default ? BusinessClock.Now() : VoucherDate,
            Type         = ProcessType.Bullion,
            Direction    = ProcessDirectionType.Inbound,
            Amount       = _amount,
            Factor       = _isReport ? _auMilyem : 0m,        // altın milyemi
            MainUnitId   = mainUnitId ?? Guid.Empty,
            PayFactor    = _goldLaborRate,                     // altın işçilik fiyatı
            PayUnitId    = payUnitId,
            PayUnitRate  = BuyOf(payUnitId),
            Description  = _description,

            // ── Takoz alanları ──
            BullionType            = _bullionType,
            AssayOfficeId          = _assayOfficeId,
            ReportNo               = _reportNo,
            IsReport               = _isReport,
            IsExtra                = _isReport && _isExtra,
            AssayAmount            = _assayAmount,
            SilverFactor           = _isReport ? _agMilyem : 0m,
            PlatinumFactor         = _isReport && _isExtra ? _ptMilyem : 0m,
            PalladiumFactor        = _isReport && _isExtra ? _pdMilyem : 0m,
            SilverMode             = _silverMode,
            PlatinumMode           = _isReport && _isExtra ? _platinumMode : (MetalDisposition?)null,
            PalladiumMode          = _isReport && _isExtra ? _palladiumMode : (MetalDisposition?)null,
            LaborMode              = _laborMode,
            SilverLaborRate        = _silverLaborRate,
            PlatinumLaborRate      = _ptLaborRate,
            PalladiumLaborRate     = _pdLaborRate,
            GoldLaborUnitId        = _goldLaborUnitId,
            SilverLaborUnitId      = _silverLaborUnitId,
            PlatinumLaborUnitId    = _ptLaborUnitId,
            PalladiumLaborUnitId   = _pdLaborUnitId,
            SilverUnitId           = UnitIdOf(CurrencyUnitCode.GUM),
            PlatinumUnitId         = UnitIdOf(CurrencyUnitCode.PLT),
            PalladiumUnitId        = UnitIdOf(CurrencyUnitCode.PLD),
            // Kur snapshot'ları (kayıt anı)
            GoldRate               = BuyOf(UnitIdOf(CurrencyUnitCode.HAS)),
            SilverRate             = BuyOf(UnitIdOf(CurrencyUnitCode.GUM)),
            PlatinumRate           = BuyOf(UnitIdOf(CurrencyUnitCode.PLT)),
            PalladiumRate          = BuyOf(UnitIdOf(CurrencyUnitCode.PLD)),
            GoldLaborUnitRate      = BuyOf(_goldLaborUnitId),
            SilverLaborUnitRate    = BuyOf(_silverLaborUnitId),
            PlatinumLaborUnitRate  = BuyOf(_ptLaborUnitId),
            PalladiumLaborUnitRate = BuyOf(_pdLaborUnitId),
        };

        var wasEdit = _editingLineId != Guid.Empty;   // Düzelt akışı mı, yeni ekleme mi?
        try
        {
            // Kararı persister verir (TEK yer): dış cari → normal fiş kaydı · iç kasa → Teyit teklifi ·
            // beyan kipi → alıcının kendi satırı. Teyit yollarında fiş OLUŞMAZ → result.Line null.
            var persisted = await Persister.PersistAsync(new VoucherLinePersistRequest(
                dto, CounterpartyVaultId, VaultId, DeclareConfirmationId));

            if (persisted.Line is not { } saved)
            {
                // Teyit kuruldu/beyan edildi ya da ön koşul sağlanmadı: fiş/grid durumu ELLENMEZ (toast persister'da).
                if (persisted.Outcome != VoucherLinePersistOutcome.Blocked)
                {
                    _editingLineId = Guid.Empty;
                    await OnConfirmationSubmitted.InvokeAsync(persisted.Outcome);
                }
                return;
            }

            VoucherId = saved.VoucherId;
            await OnSaved.InvokeAsync(saved);
            Ui.ShowSuccessToast(wasEdit ? L["Voucher_LineUpdated"].Value : L["Voucher_LineAdded"].Value);
            _editingLineId = Guid.Empty;   // sonraki kayıt yeni satır (edit modundan çık; add akışında zaten boştu)
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Ui.ShowErrorToast(L["Voucher_LineSaveFailed", ex.Message].Value);
        }
    }

    /// <summary>Kayıtlı takoz satırını forma yansıtır (Düzelt). Lookup'lar yüklenene kadar bekler (init yarışı).</summary>
    public async Task LoadForEditAsync(VoucherLineDto dto)
    {
        await _initTask;

        _bullionCode   = string.IsNullOrEmpty(dto.CommodityCode) ? null : dto.CommodityCode;
        _bullionType   = dto.BullionType ?? BullionType.Gold;
        _assayOfficeId = dto.AssayOfficeId;
        _reportNo      = dto.ReportNo;
        _isReport      = dto.IsReport ?? false;
        _isExtra       = dto.IsExtra ?? false;
        _amount        = dto.Amount;
        _assayAmount   = dto.AssayAmount ?? 0m;
        _auMilyem      = dto.Factor;
        _agMilyem      = dto.SilverFactor ?? 0m;
        _ptMilyem      = dto.PlatinumFactor ?? 0m;
        _pdMilyem      = dto.PalladiumFactor ?? 0m;
        _silverMode    = dto.SilverMode ?? MetalDisposition.Deliver;
        _platinumMode  = dto.PlatinumMode ?? MetalDisposition.Deliver;
        _palladiumMode = dto.PalladiumMode ?? MetalDisposition.Deliver;
        _laborMode     = dto.LaborMode ?? BullionLaborMode.DeductFromGold;
        _goldLaborRate   = dto.PayFactor;
        _silverLaborRate = dto.SilverLaborRate ?? 0m;
        _ptLaborRate     = dto.PlatinumLaborRate ?? 0m;
        _pdLaborRate     = dto.PalladiumLaborRate ?? 0m;
        _goldLaborUnitId   = dto.GoldLaborUnitId   ?? UnitIdOf(CurrencyUnitCode.HAS);
        _silverLaborUnitId = dto.SilverLaborUnitId ?? UnitIdOf(CurrencyUnitCode.HAS);
        _ptLaborUnitId     = dto.PlatinumLaborUnitId  ?? UnitIdOf(CurrencyUnitCode.HAS);
        _pdLaborUnitId     = dto.PalladiumLaborUnitId ?? UnitIdOf(CurrencyUnitCode.HAS);
        // Altından Düş'te server HAS'ı kendisi yazar (nakit listesinde yok) → WithCash değilse TRY default; WithCash'te saklı birim.
        _payUnitId     = _laborMode == BullionLaborMode.WithCash
            ? (dto.PayUnitId ?? UnitIdOf(CurrencyUnitCode.TRY))
            : UnitIdOf(CurrencyUnitCode.TRY);
        _description   = dto.Description;
        VoucherId      = dto.VoucherId;
        _editingLineId = dto.Id;   // UPDATE için satır Id'si korunur (diğer panellerdeki _model.Id paritesi)

        // Geçmiş satırın ayar evi pasifse/silinmişse combo'da görünsün (sentetik öğe).
        if (_assayOfficeId is { } aid && _assayOffices.All(a => a.Id != aid))
            _assayOffices.Add(new AssayOpt(aid, "?"));

        Recalc();
        StateHasChanged();
    }

    private sealed record TypeOpt(BullionType Value, string Label);
    private sealed record BoolOpt(bool Value, string Label);
    private sealed record DispOpt(MetalDisposition Value, string Label);
    private sealed record ModeOpt(BullionLaborMode Value, string Label);
    private sealed record UnitOpt(Guid Id, string Code);
    private sealed record AssayOpt(Guid Id, string Name);
    private sealed record SummaryLeg(string Code, decimal Value);
}
