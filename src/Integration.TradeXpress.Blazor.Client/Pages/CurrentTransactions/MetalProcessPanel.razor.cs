using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Maden (Metal) fiş satırı paneli — ortak iskelet ProcessPanelHostBase'te; burada maden lookup'ı,
/// milyem/Has hesabı, TEK PAY SETİ (işçilik ↔ bedel) ve Peşin/Bedelli parite bacağı var.
/// </summary>
public partial class MetalProcessPanel
{
    [Inject] private IMetalAppService MetalService { get; set; } = default!;
    [Inject] private ICashAppService CashService { get; set; } = default!;
    [Inject] private ICurrencyUnitAppService CurrencyUnitService { get; set; } = default!;
    [Inject] private IEffectivePriceAppService PriceService { get; set; } = default!;
    [Inject] private IParityAppService ParityService { get; set; } = default!;

    protected override ProcessType ProcessType => ProcessType.Metal;

    protected override VoucherLineDto CreateModel() => new()
    {
        Type        = ProcessType.Metal,
        Direction   = ProcessDirectionType.Outbound,
        PaymentType = ProcessPaymentType.Normal,
        Factor      = 0.995m,
        DueDate     = BusinessClock.Today(),
    };

    private bool _isMobile;

    private List<MetalListDto> _allMetals = new();
    private List<MetalListDto> _activeMetals = new();

    private List<CurrencyUnitListDto> _allCurrencyUnits = new();
    private List<CurrencyUnitListDto> _activeUnits = new();

    private List<CashListDto> _allCashes = new();

    // Karşı bacak (Peşin/Bedelli fiyat birimi). Peşin→Cash kayıtları, Bedelli→para birimi (ana hariç).
    private record PayComboItem(Guid Id, string Code, bool IsActive, Guid? PayUnitId, string? PayUnitCode);
    private List<PayComboItem> _activePayItems = new();
    private PayComboItem? _selectedPayItem;

    private Dictionary<Guid, decimal> _buyByUnit = new();
    private Dictionary<Guid, string>  _codeByUnit = new();
    private List<(Guid Base, Guid Quote)> _parityPairs = new();
    private Guid? _localUnitId;   // company YEREL para birimi — Peşin nakit/Bedelli combo default'u (takip birimi eşleşmesi)

    // Seçili madenin panel durumu
    private decimal _baseFactor = 0.995m;
    private MetalLaborType _laborType = MetalLaborType.Amount;
    private bool _factorReadOnly, _laborReadOnly, _amountReadOnly, _showAdet, _isQuantity;
    private decimal _stableQuantity;

    // İşçilik-rate caption'ı — Normal/İade/Emanet'te "İşçilik (Adet/Miktar)"; Peşin/Bedelli'de "PayFactor" (bedel). Tek pay seti.
    private string PayRateCaption => HasPriceMode
        ? nameof(VoucherLineDto.PayFactor)
        : $"{L["Labor"].Value} ({(_laborType == MetalLaborType.Quantity ? L["Count"].Value : L["Amount"].Value)})";

    private record DirectionItem(ProcessDirectionType Value, string Label);
    private List<DirectionItem> _directionItems = new();
    private record PaymentItem(ProcessPaymentType? Value, string Label);
    private List<PaymentItem> _paymentItems = new();

    private bool HasPriceMode =>
        Model.PaymentType is ProcessPaymentType.WithCash or ProcessPaymentType.WithCurrency;

    private decimal PureHas => Model.Amount * _baseFactor;
    private decimal LaborTotalOf(decimal rate)
        => rate * (_laborType == MetalLaborType.Quantity ? Model.Quantity : Model.Amount);

    // Peşin/Bedelli: işçilik Has'a yedirilir. İşçilik kullanıcıya gösterilmediğinden (tek pay seti bedele ayrık)
    // madenin giriş/çıkış işçilik DEFAULT'u doğrudan okunur — eski _laborRate state'i ile eşdeğer, ayrı state gerekmez.
    private decimal LaborHas()
    {
        var m = _allMetals.FirstOrDefault(x => x.Id == Model.CommodityId);
        if (m is null || Model.MainUnitId == Guid.Empty) return 0m;
        var inflow = Model.Direction.IsInflow();
        var rate   = inflow ? m.EntryLabor : m.ExitLabor;
        var lu     = (inflow ? m.EntryLaborUnitId : m.ExitLaborUnitId) ?? m.FollowingUnitId;
        var lt     = LaborTotalOf(rate);
        if (lu == Model.MainUnitId) return lt;
        var bl = BuyOf(lu); var bm = BuyOf(Model.MainUnitId);
        return bm != 0m ? lt * bl / bm : 0m;
    }

    private Task _initTask = Task.CompletedTask;
    private bool _editLoaded;   // edit yüklendiyse InitializeAsync default metal SEÇMESİN (async yarış koruması)

    protected override Task OnInitializedAsync() => _initTask = InitializeAsync();

    private async Task InitializeAsync()
    {
        _directionItems = new()
        {
            new(ProcessDirectionType.Inbound,  L["Enum:ProcessDirectionType:Inbound"].Value),
            new(ProcessDirectionType.Outbound, L["Enum:ProcessDirectionType:Outbound"].Value),
        };
        _paymentItems = new()
        {
            new(ProcessPaymentType.Normal,       L["Enum:ProcessPaymentType:Normal"].Value),
            new(ProcessPaymentType.WithCash,     L["Enum:ProcessPaymentType:WithCash"].Value),
            new(ProcessPaymentType.WithCurrency, L["Enum:ProcessPaymentType:WithCurrency"].Value),
            new(ProcessPaymentType.Return,       L["Enum:ProcessPaymentType:Return"].Value),
            new(ProcessPaymentType.Consignment,  L["Enum:ProcessPaymentType:Consignment"].Value),
            // Rezervasyon yalnız MADEN işlemlerinde seçilebilir (Muadil M0 kararı) — diğer panellere eklenmez.
            new(ProcessPaymentType.Reservation,  L["Enum:ProcessPaymentType:Reservation"].Value),
        };
        await ReloadMetalsAsync();

        var cashResult = await CashService.GetListAsync(new CashListRequestDto { MaxResultCount = 1000 });
        _allCashes = cashResult.Items.ToList();

        var unitResult = await CurrencyUnitService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _allCurrencyUnits = unitResult.Items.ToList();
        _activeUnits = _allCurrencyUnits.Where(u => u.IsActive).ToList();
        _codeByUnit = _allCurrencyUnits.ToDictionary(u => u.Id, u => u.Code);

        var prices = await PriceService.GetCurrentPricesAsync();
        _buyByUnit = prices.ToDictionary(p => p.Id, p => p.Buy);
        var parityResult = await ParityService.GetListAsync(new ParityListRequestDto { MaxResultCount = 1000 });
        _parityPairs = parityResult.Items.Select(p => (p.BaseCurrencyUnitId, p.QuoteCurrencyUnitId)).ToList();

        _localUnitId = await PriceService.GetWorkingLocalCurrencyUnitIdAsync();   // Peşin/Bedelli default karşı bacak

        if (!_editLoaded && _activeMetals.Count > 0)   // edit yükleniyorsa default metal SEÇME (loaded değerleri ezmesin)
            OnMetalChanged(_activeMetals[0].Id);
    }

    /// <summary>Maden listesini (lookup verisi) yükler/tazeler — combo, LISTELEME GRIDIYLE AYNI sırada:
    /// Kod artan (kullanıcı kararı; picker'ın birim-düzeni sırası değil). Lookup'tan ekle/düzelt sonrası
    /// EntityChange bu metodu yeniden çağırır.</summary>
    private async Task ReloadMetalsAsync()
    {
        _allMetals = await MetalService.GetPickerListAsync();
        _activeMetals = _allMetals
            .Where(m => m.IsActive)
            .OrderBy(m => m.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private decimal BuyOf(Guid id) => _buyByUnit.GetValueOrDefault(id);
    private Guid? ParityMainOf(Guid a, Guid b)
        => ParityResolver.ResolveBaseId(
            _parityPairs, a, b,
            id => CurrencyUnitPriority.RankOf(_codeByUnit.GetValueOrDefault(id, string.Empty)));

    private void Recalc(EditedField edited)
    {
        Model.EditedField = edited;

        // Adet bazlı + sabit miktar → Miktar = Adet × StableQuantity (Miktar kilitli).
        if (_isQuantity && _stableQuantity > 0m)
            Model.Amount = Model.Quantity * _stableQuantity;

        if (!HasPriceMode)
        {
            // Normal/İade/Emanet → işçilik karşı bacak (parite YOK; inline). Factor = saf milyem; işçilik = PayFactor × (adet|miktar).
            Model.Factor = _baseFactor;
            Model.Total  = PureHas;
            var qty = _laborType == MetalLaborType.Quantity ? Model.Quantity : Model.Amount;
            if (edited == EditedField.PayTotal && qty != 0m)
                Model.PayFactor = Model.PayTotal / qty;   // PayTotal elle düzenlendi → rate geri-hesap
            else
                Model.PayTotal = Model.PayFactor * qty;
            // İşçilik birimi = seçili pay item (Normal'de _activePayItems = para birimleri); PayUnitId=PayCommodityId.
            Model.PayUnitId        = _selectedPayItem?.PayUnitId ?? Model.PayCommodityId;
            Model.PayCommodityCode = _selectedPayItem?.Code
                                     ?? (Model.PayCommodityId is { } lu ? _codeByUnit.GetValueOrDefault(lu) : null);
            Model.MarketPrice = 0m;
            Model.Profit      = 0m;
            return;
        }

        // Peşin/Bedelli → işçilik Has'a çevrilip Factor'a yedirilir; karşı bacak = parite bedel.
        var total = PureHas + LaborHas();
        Model.Total  = total;
        Model.Factor = Model.Amount != 0m ? total / Model.Amount : _baseFactor;

        var payCurrency = _selectedPayItem?.PayUnitId;
        if (payCurrency is null) return;
        Model.PayUnitId        = payCurrency;
        Model.PayCommodityId   = _selectedPayItem?.Id;
        Model.PayCommodityCode = _selectedPayItem?.Code;

        var r = VoucherLineCalculator.Calculate(
            new VoucherLineCalcInput(
                ProcessType:  ProcessType.Metal,
                Direction:    Model.Direction,
                PaymentType:  Model.PaymentType,
                MainUnitId:   Model.MainUnitId,
                PayUnitId:    payCurrency,
                Amount:       total,
                Factor:       1m,
                Total:        total,
                PayFactor:    Model.PayFactor,
                PayTotal:     Model.PayTotal,
                MarketPrice:  Model.MarketPrice,
                EditedField:  edited),
            BuyOf, ParityMainOf);

        Model.PayFactor   = r.PayFactor;
        Model.PayTotal    = r.PayTotal;
        Model.MarketPrice = r.MarketPrice;
        Model.Profit      = r.Profit;
    }

    private void OnMetalChanged(Guid? id)
    {
        var m = id.HasValue ? _allMetals.FirstOrDefault(x => x.Id == id.Value) : null;
        Model.CommodityId   = id;
        Model.CommodityCode = m?.Code ?? string.Empty;
        Model.MainUnitId    = m?.FollowingUnitId ?? Guid.Empty;

        if (m is { })
        {
            _baseFactor       = m.Factor;
            _factorReadOnly   = !m.FactorChange;
            _isQuantity       = m.IsQuantity;
            _stableQuantity   = m.StableQuantity;
            _laborType        = m.LaborType;
            ApplyDirectionLabor(m);
            _showAdet         = m.IsQuantity || m.LaborType == MetalLaborType.Quantity;
            _amountReadOnly   = m.IsQuantity && m.StableQuantity > 0m;
            if (_amountReadOnly && Model.Quantity == 0m) Model.Quantity = 1m;
        }

        BuildPayList();
        EnsurePayItem();
        Recalc(EditedField.Commodity);
    }

    // Yön'e göre işçilik kilidini + (yalnız işçilik modunda) işçilik DEFAULT'unu pay alanlarına uygular.
    private void ApplyDirectionLabor(MetalListDto m)
    {
        var inflow = Model.Direction.IsInflow();   // Giriş
        _laborReadOnly = !(inflow ? m.EntryLaborChange : m.ExitLaborChange);
        if (!HasPriceMode)   // Normal/İade/Emanet → pay = işçilik; default rate/birimi Model.Pay*'e yaz (EnsurePayItem seçer)
        {
            Model.PayFactor      = inflow ? m.EntryLabor : m.ExitLabor;
            Model.PayCommodityId = (inflow ? m.EntryLaborUnitId : m.ExitLaborUnitId) ?? m.FollowingUnitId;
        }
    }

    // Peşin → Cash kayıtları (PayUnit = Cash.FollowingUnit); Bedelli → para birimi (ana hariç).
    private void BuildPayList()
    {
        if (Model.PaymentType == ProcessPaymentType.WithCash)
        {
            _activePayItems = _allCashes
                .Where(c => c.IsActive && c.FollowingUnitId != Model.MainUnitId)
                .Select(c => new PayComboItem(c.Id, c.Code, true,
                                              c.FollowingUnitId == Guid.Empty ? null : c.FollowingUnitId,
                                              c.FollowingUnitCode))
                .ToList();
        }
        else
        {
            // Ana birim dışlaması yalnız Bedelli'de (karşı bacak farklı birim olmalı); işçilik modunda
            // (Normal/İade/Emanet/Rezervasyon) TÜM birimler — madenin kendi takip/işçilik birimi de seçilebilir.
            _activePayItems = _activeUnits
                .Where(u => !HasPriceMode || u.Id != Model.MainUnitId)
                .Select(u => new PayComboItem(u.Id, u.Code, true, u.Id, u.Code))
                .ToList();
        }
    }

    private void EnsurePayItem()
    {
        if (Model.PayCommodityId is { } pcid && _activePayItems.Any(x => x.Id == pcid))
            ApplyPayItem(_activePayItems.First(x => x.Id == pcid));
        else
            ApplyPayItem(DefaultPayItem());
    }

    // Default karşı bacak: takip birimi (PayUnitId) = company YEREL para birimi olan ilk item; yoksa listenin ilki.
    // (Peşin: nakit kaydının FollowingUnit'i; Bedelli: para biriminin kendisi — ikisi de PayUnitId'de.)
    private PayComboItem? DefaultPayItem()
        => (_localUnitId is { } lu ? _activePayItems.FirstOrDefault(x => x.PayUnitId == lu) : null)
           ?? _activePayItems.FirstOrDefault();

    private void ApplyPayItem(PayComboItem? item)
    {
        _selectedPayItem       = item;
        Model.PayCommodityId   = item?.Id;
        Model.PayCommodityCode = item?.Code;
        // PayUnitId Recalc içinde payCurrency'den set edilir; burada combo seçimini tutuyoruz.
    }

    private void OnDirectionChanged(ProcessDirectionType value)
    {
        Model.Direction = value;
        var m = _allMetals.FirstOrDefault(x => x.Id == Model.CommodityId);
        if (m is { }) ApplyDirectionLabor(m);
        Recalc(EditedField.Direction);
    }

    private void OnPaymentTypeChanged(ProcessPaymentType? value)
    {
        var wasPriceMode = HasPriceMode;                 // eski mod (değişmeden önce)
        var prevPayUnit  = _selectedPayItem?.PayUnitId;  // eski karşı bacak BİRİMİ (Peşin: nakit takip birimi; Bedelli: para birimi)
        Model.PaymentType = value;                       // yeni mod

        if (HasPriceMode)
        {
            // Peşin/Bedelli → karşı bacak listesi. Önceki BİRİM (PayUnitId) yeni listede varsa KORU (item Id farklı olsa da);
            // yoksa yerel default. Her hâlde seçilen birim için PayFactor/PayTotal'ı TAZE hesapla (staleness olmasın).
            BuildPayList();
            var keep = prevPayUnit is { } pu ? _activePayItems.FirstOrDefault(x => x.PayUnitId == pu) : null;
            ApplyPayItem(keep ?? DefaultPayItem());
            Recalc(EditedField.PayUnit);
        }
        else
        {
            // Normal/İade/Emanet. Peşin/Bedelli'DEN geliyorsak madenin DEFAULT işçiliğini yaz;
            // labor↔labor (Normal↔İade↔Emanet) ise işçilik alanları DEĞİŞMEZ — yalnız yeniden hesap.
            if (wasPriceMode && _allMetals.FirstOrDefault(x => x.Id == Model.CommodityId) is { } m)
            {
                ApplyDirectionLabor(m);   // Model.PayFactor/PayCommodityId = madenin işçilik default'u
                BuildPayList();
                EnsurePayItem();
            }
            Recalc(EditedField.PaymentType);
        }
    }

    private void OnQuantityChanged(decimal value) { Model.Quantity = value; Recalc(EditedField.Amount); }
    private void OnAmountChanged(decimal value)   { Model.Amount = value;   Recalc(EditedField.Amount); }
    private void OnFactorChanged(decimal value)   { _baseFactor = value;    Recalc(EditedField.Amount); }

    // Total (HAS) elle düzenlenince Factor'u geri-hesapla. Normal: baz milyem; Peşin/Bedelli: efektif Factor.
    private void OnTotalChanged(decimal value)
    {
        if (!HasPriceMode)
        {
            _baseFactor = Model.Amount != 0m ? value / Model.Amount : _baseFactor;
            Recalc(EditedField.Amount);   // Total = Amount × baseFactor = value
        }
        else
        {
            Model.Total  = value;
            Model.Factor = Model.Amount != 0m ? value / Model.Amount : Model.Factor;
            RecalcPriceLeg(value);        // Total'ı koru, yalnız bedeli yeniden hesapla
        }
    }

    // Peşin/Bedelli: verilen Total'ı koruyarak bedel bacağını (parite) yeniden hesaplar.
    private void RecalcPriceLeg(decimal total)
    {
        var payCurrency = _selectedPayItem?.PayUnitId;
        if (payCurrency is null) return;
        Model.PayUnitId        = payCurrency;
        Model.PayCommodityId   = _selectedPayItem?.Id;
        Model.PayCommodityCode = _selectedPayItem?.Code;

        var r = VoucherLineCalculator.Calculate(
            new VoucherLineCalcInput(
                ProcessType: ProcessType.Metal, Direction: Model.Direction, PaymentType: Model.PaymentType,
                MainUnitId: Model.MainUnitId, PayUnitId: payCurrency,
                Amount: total, Factor: 1m, Total: total,
                PayFactor: Model.PayFactor, PayTotal: Model.PayTotal, MarketPrice: Model.MarketPrice,
                EditedField: EditedField.Amount),
            BuyOf, ParityMainOf);

        Model.PayFactor   = r.PayFactor;
        Model.PayTotal    = r.PayTotal;
        Model.MarketPrice = r.MarketPrice;
        Model.Profit      = r.Profit;
    }

    private void OnPayItemChanged(Guid? id)
    {
        ApplyPayItem(id.HasValue ? _activePayItems.FirstOrDefault(x => x.Id == id.Value) : null);
        Recalc(EditedField.PayUnit);
    }

    private void OnPayFactorChanged(decimal value) { Model.PayFactor = value; Recalc(EditedField.PayFactor); }
    private void OnPayTotalChanged(decimal value)  { Model.PayTotal = value;  Recalc(EditedField.PayTotal); }

    // Ortak panel stilleri (ProcessPanelStyles SSOT) — Metal alan bazında farklı genişlik kullanır (60/240px).
    private string GroupStyle()          => ProcessPanelStyles.Group(_isMobile);
    private string GroupStyle(int w)     => ProcessPanelStyles.Group(_isMobile, w);
    private string ControlStyle()        => ProcessPanelStyles.Control(_isMobile);

    // ── Base kancaları (HandleSave / LoadForEditAsync iskeleti ProcessPanelHostBase'te) ──

    protected override bool CanSave()
    {
        if (Model.CommodityId is null || Model.MainUnitId == Guid.Empty)
            return false;
        if (HasPriceMode && _selectedPayItem is null)
            return false;
        return true;
    }

    protected override void PrepareModelForSave()
    {
        Recalc(Model.EditedField);   // son durum garanti
        Model.PayUnitRate = Model.PayUnitId is { } pu ? BuyOf(pu) : 0m;
    }

    protected override void OnAfterSavePersisted()
    {
        Model.EditedField = EditedField.None;
    }

    protected override void ResetVolatileFields()
    {
        Model.Amount      = 0m;
        Model.Quantity    = 0m;
        Model.PayTotal    = 0m;
        Model.Description = null;
        Recalc(EditedField.None);
    }

    protected override async Task OnLoadedForEditAsync(VoucherLineDto dto)
    {
        _editLoaded = true;        // OnInitialized'ın default metal seçimini iptal et (yarışı kapat)
        await _initTask;           // veriler (özellikle _allMetals) yüklensin → m bulunur, default ezme olmaz

        var m = _allMetals.FirstOrDefault(x => x.Id == dto.CommodityId);
        if (m is { })
        {
            Model.MainUnitId  = m.FollowingUnitId;           // ana birim garanti (BuildPayList filtresi buna bağlı)
            _factorReadOnly   = !m.FactorChange;
            _isQuantity       = m.IsQuantity;
            _stableQuantity   = m.StableQuantity;
            _laborType        = m.LaborType;
            var inflow        = Model.Direction.IsInflow();
            _laborReadOnly    = !(inflow ? m.EntryLaborChange : m.ExitLaborChange);
            _showAdet         = m.IsQuantity || m.LaborType == MetalLaborType.Quantity;
            _amountReadOnly   = m.IsQuantity && m.StableQuantity > 0m;
        }
        _baseFactor = HasPriceMode ? (m?.Factor ?? dto.Factor) : dto.Factor;   // Normal=saved milyem; Peşin/Bedelli=pür milyem

        // TEK PAY SETİ (ERPPROV3) → her iki mod aynı: Model.Pay* zaten dto'da; ayrı state reconstruction YOK.
        // Pay listesini kur + saved PayCommodityId'yi seç (aktif listede yoksa kaybolmasın diye başa ekle).
        BuildPayList();
        if (Model.PayCommodityId is { } pcid && _activePayItems.All(x => x.Id != pcid))
            _activePayItems.Insert(0, new PayComboItem(pcid, Model.PayCommodityCode ?? string.Empty, true, Model.PayUnitId, Model.PayUnitCode));
        _selectedPayItem = _activePayItems.FirstOrDefault(x => x.Id == Model.PayCommodityId);
    }
}
