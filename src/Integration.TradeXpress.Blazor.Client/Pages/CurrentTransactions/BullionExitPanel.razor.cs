using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Bullions;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

public partial class BullionExitPanel : IVoucherLineEditPanel
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
    private List<DispOpt>  _dispositions  = new();
    private List<ModeOpt>  _laborModes    = new();
    private List<UnitOpt>  _allUnitOptions  = new();
    private List<UnitOpt>  _cashUnitOptions = new();
    private List<BullionStockItemDto> _stock = new();
    private Dictionary<Guid, decimal> _buyByUnit = new();   // birim Id → alış kuru (kayıt anı snapshot)

    // ── Seçili külçe snapshot'ı — metal verisi server otoritedir; panelde yalnız Save'e taşınan
    //    FIELD'lar + net-özet HESABI için gereken ham metal alanları tutulur.
    //    ⚠ Bu metal alanları EKRANDA GÖSTERİLMEZ (kimlik/milyem/has göstergesi yok) — yalnız
    //       ComputeBullion'a girdi (net sonuç özeti için). ──
    private Guid?            _entryLineId;
    private string?          _code;
    private BullionType      _bullionType = BullionType.Gold;
    private bool             _isReport;
    private bool             _isExtra;
    private bool             _isEdit;
    private bool             _hasSelection => _isEdit || _entryLineId is not null;
    // Net-özet hesabı için ham metal alanları (gösterilmez).
    private decimal          _amount;
    private decimal          _auMilyem;
    private decimal          _agMilyem;
    private decimal          _ptMilyem;
    private decimal          _pdMilyem;

    // ── Kullanıcı girişi (işçilik + durumlar) ──
    private MetalDisposition _silverMode    = MetalDisposition.Deliver;
    private MetalDisposition _platinumMode  = MetalDisposition.Deliver;
    private MetalDisposition _palladiumMode = MetalDisposition.Deliver;
    private decimal          _goldLaborRate;
    private Guid?            _goldLaborUnitId;
    private decimal          _silverLaborRate;
    private Guid?            _silverLaborUnitId;
    private decimal          _ptLaborRate;          // motor 4-metal işçilikli (giriş paneliyle tutarlı)
    private Guid?            _ptLaborUnitId;
    private decimal          _pdLaborRate;
    private Guid?            _pdLaborUnitId;
    private BullionLaborMode _laborMode = BullionLaborMode.DeductFromGold;
    private Guid?            _payUnitId;
    private string?          _description;
    private string?          _error;

    // ── Net sonuç özeti (ERPPRO iFTakozGiris canlı grid paritesi; giriş paneliyle simetrik) ──
    private List<SummaryLeg> _summaryLegs = new();

    private Task _initTask = Task.CompletedTask;
    protected override Task OnInitializedAsync() => _initTask = InitializeAsync();

    private async Task InitializeAsync()
    {
        _dispositions = new()
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

        var prices = await PriceService.GetCurrentPricesAsync();
        _buyByUnit = prices.ToDictionary(p => p.Id, p => p.Buy);

        // Düzeltmede de stok filtresiz çekilir → seçili külçenin rapor/extra bayrakları buradan dolar.
        _stock = await VoucherService.GetBullionStockAsync(inStock: _isEdit ? null : true);

        _goldLaborUnitId   ??= UnitIdOf(CurrencyUnitCode.HAS);
        _silverLaborUnitId ??= UnitIdOf(CurrencyUnitCode.HAS);
        _ptLaborUnitId     ??= UnitIdOf(CurrencyUnitCode.HAS);
        _pdLaborUnitId     ??= UnitIdOf(CurrencyUnitCode.HAS);
        _payUnitId         ??= UnitIdOf(CurrencyUnitCode.TRY);
        Recalc();
    }

    private Guid? UnitIdOf(string code)
        => _allUnitOptions.FirstOrDefault(u => string.Equals(u.Code, code, StringComparison.OrdinalIgnoreCase))?.Id;

    private string? UnitCodeOf(Guid? id)
        => id is { } u ? _allUnitOptions.FirstOrDefault(x => x.Id == u)?.Code : null;

    private decimal BuyOf(Guid? id) => id is { } u ? _buyByUnit.GetValueOrDefault(u) : 0m;

    // Kod → alış kuru; TRY kuru tanımsızsa 1 (yerel para — ERPPRO GetKur paritesi).
    private decimal RateOf(string? code)
    {
        if (string.IsNullOrEmpty(code)) return 0m;
        var buy = BuyOf(UnitIdOf(code));
        return buy == 0m && string.Equals(code, CurrencyUnitCode.TRY, StringComparison.OrdinalIgnoreCase) ? 1m : buy;
    }

    // ── Event'ler ──
    private void OnGoldLaborRateChanged(decimal v)   { _goldLaborRate = v; Recalc(); }
    private void OnSilverLaborRateChanged(decimal v) { _silverLaborRate = v; Recalc(); }
    private void OnPtLaborRateChanged(decimal v)     { _ptLaborRate = v; Recalc(); }
    private void OnPdLaborRateChanged(decimal v)     { _pdLaborRate = v; Recalc(); }

    private void OnBullionChanged(Guid? entryLineId)
    {
        _entryLineId = entryLineId;
        var item = _stock.FirstOrDefault(s => s.EntryLineId == entryLineId);
        ApplyStockItem(item);
        Recalc();
    }

    private void OnLaborModeChanged(BullionLaborMode v)
    {
        _laborMode = v;
        if (_laborMode == BullionLaborMode.WithCash && _payUnitId is null)
            _payUnitId = UnitIdOf(CurrencyUnitCode.TRY);
        Recalc();
    }

    /// <summary>Seçilen külçenin rapor/extra bayrakları + net-özet hesabı için ham metal alanlarını alır
    /// (metal alanları EKRANDA gösterilmez; yalnız ComputeBullion girdisi).</summary>
    private void ApplyStockItem(BullionStockItemDto? item)
    {
        _code        = item?.Code;
        _bullionType = item?.BullionType ?? BullionType.Gold;
        _isReport    = item?.IsReport ?? false;
        _isExtra     = item?.IsExtra ?? false;
        _amount      = item?.Amount ?? 0m;
        _auMilyem    = item?.GoldFactor ?? 0m;
        _agMilyem    = item?.SilverFactor ?? 0m;
        _ptMilyem    = item?.PlatinumFactor ?? 0m;
        _pdMilyem    = item?.PalladiumFactor ?? 0m;

        // Extra olmayan külçede Pt/Pd durum+işçilik etkisiz; seçim temizlenince default'a döner (legacy FormClear).
        if (item is null || !item.IsExtra)
        {
            _platinumMode  = MetalDisposition.Deliver;
            _palladiumMode = MetalDisposition.Deliver;
            _ptLaborRate   = 0m;
            _pdLaborRate   = 0m;
        }
        if (item is null)
            _silverMode = MetalDisposition.Deliver;
    }

    // ── Net sonuç özeti — sunucuyla TEK kaynak motor (ComputeBullion, Direction=Out; çeşni çıkışta eklenmez).
    //    ERPPRO iFTakozGiris canlı net grid'inin (işçilik toplamı + yan-metal net etki) karşılığı; GİRİŞ paneli
    //    (BullionProcessPanel) özet deseniyle birebir. Ara milyem/has GÖSTERİLMEZ — yalnız birim-başı net bacaklar.
    private void Recalc()
    {
        _summaryLegs = new();

        var goldRate   = RateOf(CurrencyUnitCode.HAS);
        var silverRate = RateOf(CurrencyUnitCode.GUM);
        var ptRate     = RateOf(CurrencyUnitCode.PLT);
        var pdRate     = RateOf(CurrencyUnitCode.PLD);

        // İşçilik tahsil birimi: Altından Düş → HAS; Para İle → seçilen nakit birim.
        var laborPayCode = _laborMode == BullionLaborMode.DeductFromGold
            ? CurrencyUnitCode.HAS
            : UnitCodeOf(_payUnitId);
        var payRate = _laborMode == BullionLaborMode.DeductFromGold ? goldRate : RateOf(laborPayCode);

        // ÇIKIŞTA çeşni bakiyeye eklenmez → has'lar yalnız Amount üzerinden; raporsuz külçede metal yok.
        var auMilyem = _isReport ? _auMilyem : 0m;
        var agMilyem = _isReport ? _agMilyem : 0m;
        var ptMilyem = _isReport && _isExtra ? _ptMilyem : 0m;
        var pdMilyem = _isReport && _isExtra ? _pdMilyem : 0m;
        var ptLaborRate = _isExtra ? _ptLaborRate : 0m;
        var pdLaborRate = _isExtra ? _pdLaborRate : 0m;

        var legs = BullionLegCalculator.ComputeBullion(new BullionLegInput(
            Direction:              ProcessDirectionType.Outbound,
            IsReport:               _isReport,
            Amount:                 _amount,
            AssayAmount:            0m,                // motor çıkışta çeşni eklemez zaten
            GoldFactor:             auMilyem,
            SilverFactor:           agMilyem,
            PlatinumFactor:         ptMilyem,
            PalladiumFactor:        pdMilyem,
            SilverMode:             _silverMode,
            PlatinumMode:           _platinumMode,
            PalladiumMode:          _palladiumMode,
            GoldLaborRate:          _goldLaborRate,
            SilverLaborRate:        _silverLaborRate,
            PlatinumLaborRate:      ptLaborRate,
            PalladiumLaborRate:     pdLaborRate,
            GoldRate:               goldRate,
            SilverRate:             silverRate,
            PlatinumRate:           ptRate,
            PalladiumRate:          pdRate,
            PayUnitRate:            payRate,
            GoldLaborUnitRate:      RateOf(UnitCodeOf(_goldLaborUnitId)),
            SilverLaborUnitRate:    RateOf(UnitCodeOf(_silverLaborUnitId)),
            PlatinumLaborUnitRate:  RateOf(UnitCodeOf(_ptLaborUnitId)),
            PalladiumLaborUnitRate: RateOf(UnitCodeOf(_pdLaborUnitId))));

        AddLeg(BullionConsts.PseudoUnitCode, legs.UnreportedTotal);   // raporsuz külçe (ham TAKOZ birimi)
        AddLeg(CurrencyUnitCode.HAS, legs.GoldTotal);
        AddLeg(CurrencyUnitCode.GUM, legs.SilverTotal);
        AddLeg(CurrencyUnitCode.PLT, legs.PlatinumTotal);
        AddLeg(CurrencyUnitCode.PLD, legs.PalladiumTotal);
        AddLeg(laborPayCode,         legs.LaborTotal);

        StateHasChanged();

        void AddLeg(string? code, decimal value)
        {
            if (value == 0m || string.IsNullOrEmpty(code)) return;
            var existing = _summaryLegs.FirstOrDefault(x => x.Code == code);
            if (existing is not null) { _summaryLegs[_summaryLegs.IndexOf(existing)] = existing with { Value = existing.Value + value }; return; }
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
        if (_entryLineId is null && !_isEdit) { _error = L["Bullion_Exit_SelectRequired"].Value; return; }
        if (_laborMode == BullionLaborMode.WithCash && _payUnitId is null &&
            (_goldLaborRate != 0m || _silverLaborRate != 0m || _ptLaborRate != 0m || _pdLaborRate != 0m))
        {
            _error = L["Voucher_Op_PayUnitRequired"].Value;
            return;
        }

        var mainUnitId = _isReport
            ? (UnitIdOf(_bullionType.MainUnitCode()) ?? UnitIdOf(CurrencyUnitCode.HAS))
            : (Guid?)BullionConsts.PseudoUnitId;
        var payUnitId  = _laborMode == BullionLaborMode.DeductFromGold ? UnitIdOf(CurrencyUnitCode.HAS) : _payUnitId;

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
            Type         = ProcessType.Bullion,
            Direction    = ProcessDirectionType.Outbound,

            // Külçe referansı: CommodityId = giriş satırı. Metal verisi (miktar/milyem/rapor) SUNUCUDA
            // giriş satırından kopyalanır (PrepareBullionExitLineAsync) — panel yalnız işçilik + durum gönderir.
            CommodityId  = _entryLineId,
            MainUnitId   = mainUnitId ?? Guid.Empty,
            PayFactor    = _goldLaborRate,                     // altın işçilik fiyatı
            PayUnitId    = payUnitId,
            PayUnitRate  = BuyOf(payUnitId),
            Description  = _description,

            // ── İşçilik + dağıtım durumları (kullanıcı girişi) ──
            LaborMode              = _laborMode,
            SilverMode             = _isReport ? _silverMode : (MetalDisposition?)null,
            PlatinumMode           = _isReport && _isExtra ? _platinumMode : (MetalDisposition?)null,
            PalladiumMode          = _isReport && _isExtra ? _palladiumMode : (MetalDisposition?)null,
            SilverLaborRate        = _silverLaborRate,
            PlatinumLaborRate      = _ptLaborRate,
            PalladiumLaborRate     = _pdLaborRate,
            GoldLaborUnitId        = _goldLaborUnitId,
            SilverLaborUnitId      = _silverLaborUnitId,
            PlatinumLaborUnitId    = _ptLaborUnitId,
            PalladiumLaborUnitId   = _pdLaborUnitId,
            // Kur snapshot'ları (kayıt anı) — işçilik bacağı dönüşümü için.
            GoldRate               = BuyOf(UnitIdOf(CurrencyUnitCode.HAS)),
            SilverRate             = BuyOf(UnitIdOf(CurrencyUnitCode.GUM)),
            PlatinumRate           = BuyOf(UnitIdOf(CurrencyUnitCode.PLT)),
            PalladiumRate          = BuyOf(UnitIdOf(CurrencyUnitCode.PLD)),
            GoldLaborUnitRate      = BuyOf(_goldLaborUnitId),
            SilverLaborUnitRate    = BuyOf(_silverLaborUnitId),
            PlatinumLaborUnitRate  = BuyOf(_ptLaborUnitId),
            PalladiumLaborUnitRate = BuyOf(_pdLaborUnitId),
        };

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
                    await OnConfirmationSubmitted.InvokeAsync(persisted.Outcome);
                }
                return;
            }

            VoucherId = saved.VoucherId;
            await OnSaved.InvokeAsync(saved);

            if (_isEdit)
                return;

            // Legacy: kayıt sonrası StokRefresh + form temiz, panel açık (ardışık çıkış).
            _entryLineId = null;
            ApplyStockItem(null);
            _description = null;
            _stock = await VoucherService.GetBullionStockAsync(inStock: true);
            var freshPrices = await PriceService.GetCurrentPricesAsync();
            _buyByUnit = freshPrices.ToDictionary(p => p.Id, p => p.Buy);
            Recalc();
        }
        catch (Exception ex) { _error = ex.Message; }
    }

    private Guid _editingLineId;

    /// <summary>Kayıtlı takoz çıkış satırını forma yansıtır (Düzelt). Külçe değişmez (combo gizli, kırmızı kod).</summary>
    public async Task LoadForEditAsync(VoucherLineDto dto)
    {
        _isEdit        = true;
        await _initTask;

        _editingLineId = dto.Id;
        _entryLineId   = dto.CommodityId;
        _code          = string.IsNullOrEmpty(dto.CommodityCode) ? null : dto.CommodityCode;

        // Rapor/extra bayrakları + net-özet hesabı için ham metal alanları satırdan (gösterilmez).
        _bullionType = dto.BullionType ?? BullionType.Gold;
        _isReport    = dto.IsReport ?? false;
        _isExtra     = dto.IsExtra ?? false;
        _amount      = dto.Amount;
        _auMilyem    = dto.Factor;
        _agMilyem    = dto.SilverFactor ?? 0m;
        _ptMilyem    = dto.PlatinumFactor ?? 0m;
        _pdMilyem    = dto.PalladiumFactor ?? 0m;

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
        _payUnitId     = _laborMode == BullionLaborMode.WithCash
            ? (dto.PayUnitId ?? UnitIdOf(CurrencyUnitCode.TRY))
            : UnitIdOf(CurrencyUnitCode.TRY);
        _description   = dto.Description;
        VoucherId      = dto.VoucherId;

        Recalc();
        StateHasChanged();
    }

    private sealed record DispOpt(MetalDisposition Value, string Label);
    private sealed record ModeOpt(BullionLaborMode Value, string Label);
    private sealed record UnitOpt(Guid Id, string Code);
    private sealed record SummaryLeg(string Code, decimal Value);
}
