using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.Framework.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Blazor.Client.Pages.Metals;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Maden (Metal) fiÅŸ satÄ±rÄ± paneli â€” ortak iskelet ProcessPanelHostBase'te; burada maden lookup'Ä±,
/// milyem/Has hesabÄ±, TEK PAY SETÄ° (iÅŸÃ§ilik â†” bedel) ve PeÅŸin/Bedelli parite bacaÄŸÄ± var.
/// </summary>
public partial class MetalProcessPanel
{
    [Inject] private IMetalAppService MetalService { get; set; } = default!;
    [Inject] private ICashAppService CashService { get; set; } = default!;
    [Inject] private ICurrencyUnitAppService CurrencyUnitService { get; set; } = default!;
    [Inject] private IEffectivePriceAppService PriceService { get; set; } = default!;
    [Inject] private IParityAppService ParityService { get; set; } = default!;
    [Inject] private IViewOpener ViewOpener { get; set; } = default!;   // varyant lookup âœ/+ â†’ maden kartÄ± popup'Ä±
    [Inject] private IPopupService PopupService { get; set; } = default!;

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
    private List<CommodityVariantOptionDto> _variantOptions = new();   // seÃ§ili madenin AKTÄ°F varyantlarÄ± (varyant combo'su)

    private List<CurrencyUnitListDto> _allCurrencyUnits = new();
    private List<CurrencyUnitListDto> _activeUnits = new();

    private List<CashListDto> _allCashes = new();

    // KarÅŸÄ± bacak (PeÅŸin/Bedelli fiyat birimi). PeÅŸinâ†’Cash kayÄ±tlarÄ±, Bedelliâ†’para birimi (ana hariÃ§).
    private record PayComboItem(Guid Id, string Code, bool IsActive, Guid? PayUnitId, string? PayUnitCode);
    private List<PayComboItem> _activePayItems = new();
    private PayComboItem? _selectedPayItem;

    private Dictionary<Guid, decimal> _buyByUnit = new();
    private Dictionary<Guid, string>  _codeByUnit = new();
    private List<(Guid Base, Guid Quote)> _parityPairs = new();
    private Guid? _localUnitId;   // company YEREL para birimi â€” PeÅŸin nakit/Bedelli combo default'u (takip birimi eÅŸleÅŸmesi)

    // SeÃ§ili madenin panel durumu
    private decimal _baseFactor = 0.995m;
    private MetalLaborType _laborType = MetalLaborType.Amount;
    private bool _factorReadOnly, _laborReadOnly, _amountReadOnly, _showAdet, _isQuantity;
    private decimal _stableQuantity;

    // Ä°ÅŸÃ§ilik-rate caption'Ä± â€” Normal/Ä°ade/Emanet'te "Ä°ÅŸÃ§ilik (Adet/Miktar)"; PeÅŸin/Bedelli'de "PayFactor" (bedel). Tek pay seti.
    private string PayRateCaption => HasPriceMode
        ? L["PayFactor:MetalPanel"].Value
        : $"{L["Labor:MetalPanel"].Value} ({(_laborType == MetalLaborType.Quantity ? L["Count:MetalPanel"].Value : L["Amount:MetalPanel"].Value)})";

    private string PayTotalCaption => HasPriceMode
        ? L["PayTotal:MetalPanel:Cash"].Value
        : L["PayTotal:MetalPanel:Labor"].Value;

    private string TotalCaption
    {
        get
        {
            var m = _allMetals.FirstOrDefault(x => x.Id == Model.CommodityId);
            if (m is not null && !string.IsNullOrWhiteSpace(m.FollowingUnitCode))
            {
                return $"{L["Total:MetalPanel"].Value} ({m.FollowingUnitCode})";
            }
            return L["Total:MetalPanel"].Value;
        }
    }

    private record DirectionItem(ProcessDirectionType Value, string Label);
    private List<DirectionItem> _directionItems = new();
    private record PaymentItem(ProcessPaymentType? Value, string Label);
    private List<PaymentItem> _paymentItems = new();

    private bool HasPriceMode =>
        Model.PaymentType is ProcessPaymentType.WithCash or ProcessPaymentType.WithCurrency;

    private decimal PureHas => Model.Amount * _baseFactor;
    private decimal LaborTotalOf(decimal rate)
        => rate * (_laborType == MetalLaborType.Quantity ? Model.Quantity : Model.Amount);

    // PeÅŸin/Bedelli: iÅŸÃ§ilik Has'a yedirilir. Ä°ÅŸÃ§ilik kullanÄ±cÄ±ya gÃ¶sterilmediÄŸinden (tek pay seti bedele ayrÄ±k)
    // madenin giriÅŸ/Ã§Ä±kÄ±ÅŸ iÅŸÃ§ilik DEFAULT'u doÄŸrudan okunur â€” eski _laborRate state'i ile eÅŸdeÄŸer, ayrÄ± state gerekmez.
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
    private bool _editLoaded;   // edit yÃ¼klendiyse InitializeAsync default metal SEÃ‡MESÄ°N (async yarÄ±ÅŸ korumasÄ±)

    protected override Task OnInitializedAsync() => _initTask = InitializeAsync();

    private async Task InitializeAsync()
    {
        _directionItems = new()
        {
            new(ProcessDirectionType.Inbound,  L["UI:ProcessDirectionType:Inbound"].Value),
            new(ProcessDirectionType.Outbound, L["UI:ProcessDirectionType:Outbound"].Value),
        };
        _paymentItems = new()
        {
            new(ProcessPaymentType.Normal,       L["UI:ProcessPaymentType:Normal"].Value),
            new(ProcessPaymentType.WithCash,     L["UI:ProcessPaymentType:WithCash"].Value),
            new(ProcessPaymentType.WithCurrency, L["UI:ProcessPaymentType:WithCurrency"].Value),
            new(ProcessPaymentType.Return,       L["UI:ProcessPaymentType:Return"].Value),
            new(ProcessPaymentType.Consignment,  L["UI:ProcessPaymentType:Consignment"].Value),
            // Rezervasyon yalnÄ±z MADEN iÅŸlemlerinde seÃ§ilebilir (Muadil M0 kararÄ±) â€” diÄŸer panellere eklenmez.
            new(ProcessPaymentType.Reservation,  L["UI:ProcessPaymentType:Reservation"].Value),
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

        _localUnitId = await PriceService.GetWorkingLocalCurrencyUnitIdAsync();   // PeÅŸin/Bedelli default karÅŸÄ± bacak

        if (!_editLoaded && _activeMetals.Count > 0)   // edit yÃ¼kleniyorsa default metal SEÃ‡ME (loaded deÄŸerleri ezmesin)
        {
            await OnMetalChangedAsync(_activeMetals[0].Id);
        }
    }

    /// <summary>Maden listesini (lookup verisi) yÃ¼kler/tazeler â€” combo, LISTELEME GRIDIYLE AYNI sÄ±rada:
    /// Kod artan (kullanÄ±cÄ± kararÄ±; picker'Ä±n birim-dÃ¼zeni sÄ±rasÄ± deÄŸil). Lookup'tan ekle/dÃ¼zelt sonrasÄ±
    /// EntityChange bu metodu yeniden Ã§aÄŸÄ±rÄ±r.</summary>
    private async Task ReloadMetalsAsync()
    {
        _allMetals = await MetalService.GetPickerListAsync();
        _activeMetals = _allMetals
            .Where(m => m.IsActive)
            .OrderBy(m => m.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Maden lookup âœ/+ sonrasÄ± seÃ§ili madenin varyantlarÄ±nÄ± da tazele (varyant combo bayat kalmasÄ±n).
        if (Model.CommodityId is { } id)
        {
            _variantOptions = await MetalService.GetVariantPickerListAsync(id);
        }
    }

    private decimal BuyOf(Guid id) => _buyByUnit.GetValueOrDefault(id);
    private Guid? ParityMainOf(Guid a, Guid b)
        => ParityResolver.ResolveBaseId(
            _parityPairs, a, b,
            id => CurrencyUnitPriority.RankOf(_codeByUnit.GetValueOrDefault(id, string.Empty)));

    private void Recalc(EditedField edited)
    {
        Model.EditedField = edited;

        // Adet bazlÄ± + sabit miktar â†’ Miktar = Adet Ã— StableQuantity (Miktar kilitli).
        if (_isQuantity && _stableQuantity > 0m)
            Model.Amount = Model.Quantity * _stableQuantity;

        if (!HasPriceMode)
        {
            // Normal/Ä°ade/Emanet â†’ iÅŸÃ§ilik karÅŸÄ± bacak (parite YOK; inline). Factor = saf milyem; iÅŸÃ§ilik = PayFactor Ã— (adet|miktar).
            Model.Factor = _baseFactor;
            Model.Total  = PureHas;
            var qty = _laborType == MetalLaborType.Quantity ? Model.Quantity : Model.Amount;
            if (edited == EditedField.PayTotal && qty != 0m)
                Model.PayFactor = Model.PayTotal / qty;   // PayTotal elle dÃ¼zenlendi â†’ rate geri-hesap
            else
                Model.PayTotal = Model.PayFactor * qty;
            // Ä°ÅŸÃ§ilik birimi = seÃ§ili pay item (Normal'de _activePayItems = para birimleri); PayUnitId=PayCommodityId.
            Model.PayUnitId        = _selectedPayItem?.PayUnitId ?? Model.PayCommodityId;
            Model.PayCommodityCode = _selectedPayItem?.Code
                                     ?? (Model.PayCommodityId is { } lu ? _codeByUnit.GetValueOrDefault(lu) : null);
            Model.MarketPrice = 0m;
            Model.Profit      = 0m;
            return;
        }

        // PeÅŸin/Bedelli â†’ iÅŸÃ§ilik Has'a Ã§evrilip Factor'a yedirilir; karÅŸÄ± bacak = parite bedel.
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

    // Maden seÃ§imi (async sarmal) â€” sync mantÄ±k (fiyat/milyem/pay) + AKTÄ°F varyantlarÄ± yÃ¼kle. FiyatÄ± DEÄÄ°ÅTÄ°RMEZ (maden fiyatÄ± milyem/iÅŸÃ§ilik).
    private async Task OnMetalChangedAsync(Guid? id)
    {
        OnMetalChanged(id);
        await LoadVariantOptionsAsync(id);
    }

    // SeÃ§ili madenin AKTÄ°F varyantlarÄ±nÄ± yÃ¼kler. Tek varyant â†’ VariantId null (anlamlÄ± boyut yok); Ã§oklu â†’ ana varyant varsayÄ±lan.
    private async Task LoadVariantOptionsAsync(Guid? metalId)
    {
        Model.VariantId = null;
        Model.VariantCode = null;
        _variantOptions = new();
        if (metalId is { } id)
        {
            _variantOptions = await MetalService.GetVariantPickerListAsync(id);
            if (_variantOptions.Count > 1)
            {
                var main = _variantOptions.FirstOrDefault(v => v.IsMain) ?? _variantOptions[0];
                Model.VariantId = main.Id;
                Model.VariantCode = main.Code;
            }
        }
    }

    private void OnVariantChanged(Guid? id)
    {
        var v = id.HasValue ? _variantOptions.FirstOrDefault(x => x.Id == id.Value) : null;
        Model.VariantId = v?.Id;
        Model.VariantCode = v?.Code;
        // Maden fiyatÄ± milyem/iÅŸÃ§ilik â€” varyant seÃ§imi fiyatÄ± DEÄÄ°ÅTÄ°RMEZ (yalnÄ±z hangi SKU olduÄŸunu kaydeder).
    }

    // Varyant lookup âœ/+ â†’ seÃ§ili madenin KARTINI aÃ§ar (varyant yÃ¶netimi orada; commodity id ile â€” varyant id DEÄÄ°L).
    private Task OpenMetalCardAsync()
    {
        if (Model.CommodityId is not { } id)
        {
            return Task.CompletedTask;
        }

        var extra = new Dictionary<string, object>
        {
            { "OnClosed", EventCallback.Factory.Create(this, () => PopupService.Close()) },
        };
        return ViewOpener.OpenAsync(typeof(MetalEditHost), id, string.Empty, null, extra);
    }

    private async Task<Guid?> OpenMetalCardForAddAsync()
    {
        await OpenMetalCardAsync();
        return null;
    }

    // Varyant lookup tazeleme kancasÄ± â€” seÃ§ili madenin varyantlarÄ±nÄ± yeniden yÃ¼kler.
    private async Task ReloadVariantsForCurrentMetalAsync()
    {
        if (Model.CommodityId is { } id)
        {
            _variantOptions = await MetalService.GetVariantPickerListAsync(id);
            StateHasChanged();
        }
    }

    // YÃ¶n'e gÃ¶re iÅŸÃ§ilik kilidini + (yalnÄ±z iÅŸÃ§ilik modunda) iÅŸÃ§ilik DEFAULT'unu pay alanlarÄ±na uygular.
    private void ApplyDirectionLabor(MetalListDto m)
    {
        var inflow = Model.Direction.IsInflow();   // GiriÅŸ
        _laborReadOnly = !(inflow ? m.EntryLaborChange : m.ExitLaborChange);
        if (!HasPriceMode)   // Normal/Ä°ade/Emanet â†’ pay = iÅŸÃ§ilik; default rate/birimi Model.Pay*'e yaz (EnsurePayItem seÃ§er)
        {
            Model.PayFactor      = inflow ? m.EntryLabor : m.ExitLabor;
            Model.PayCommodityId = (inflow ? m.EntryLaborUnitId : m.ExitLaborUnitId) ?? m.FollowingUnitId;
        }
    }

    // PeÅŸin â†’ Cash kayÄ±tlarÄ± (PayUnit = Cash.FollowingUnit); Bedelli â†’ para birimi (ana hariÃ§).
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
            // Ana birim dÄ±ÅŸlamasÄ± yalnÄ±z Bedelli'de (karÅŸÄ± bacak farklÄ± birim olmalÄ±); iÅŸÃ§ilik modunda
            // (Normal/Ä°ade/Emanet/Rezervasyon) TÃœM birimler â€” madenin kendi takip/iÅŸÃ§ilik birimi de seÃ§ilebilir.
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

    // Default karÅŸÄ± bacak: takip birimi (PayUnitId) = company YEREL para birimi olan ilk item; yoksa listenin ilki.
    // (PeÅŸin: nakit kaydÄ±nÄ±n FollowingUnit'i; Bedelli: para biriminin kendisi â€” ikisi de PayUnitId'de.)
    private PayComboItem? DefaultPayItem()
        => (_localUnitId is { } lu ? _activePayItems.FirstOrDefault(x => x.PayUnitId == lu) : null)
           ?? _activePayItems.FirstOrDefault();

    private void ApplyPayItem(PayComboItem? item)
    {
        _selectedPayItem       = item;
        Model.PayCommodityId   = item?.Id;
        Model.PayCommodityCode = item?.Code;
        // PayUnitId Recalc iÃ§inde payCurrency'den set edilir; burada combo seÃ§imini tutuyoruz.
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
        var wasPriceMode = HasPriceMode;                 // eski mod (deÄŸiÅŸmeden Ã¶nce)
        var prevPayUnit  = _selectedPayItem?.PayUnitId;  // eski karÅŸÄ± bacak BÄ°RÄ°MÄ° (PeÅŸin: nakit takip birimi; Bedelli: para birimi)
        Model.PaymentType = value;                       // yeni mod

        if (HasPriceMode)
        {
            // PeÅŸin/Bedelli â†’ karÅŸÄ± bacak listesi. Ã–nceki BÄ°RÄ°M (PayUnitId) yeni listede varsa KORU (item Id farklÄ± olsa da);
            // yoksa yerel default. Her hÃ¢lde seÃ§ilen birim iÃ§in PayFactor/PayTotal'Ä± TAZE hesapla (staleness olmasÄ±n).
            BuildPayList();
            var keep = prevPayUnit is { } pu ? _activePayItems.FirstOrDefault(x => x.PayUnitId == pu) : null;
            ApplyPayItem(keep ?? DefaultPayItem());
            Recalc(EditedField.PayUnit);
        }
        else
        {
            // Normal/Ä°ade/Emanet. PeÅŸin/Bedelli'DEN geliyorsak madenin DEFAULT iÅŸÃ§iliÄŸini yaz;
            // laborâ†”labor (Normalâ†”Ä°adeâ†”Emanet) ise iÅŸÃ§ilik alanlarÄ± DEÄÄ°ÅMEZ â€” yalnÄ±z yeniden hesap.
            if (wasPriceMode && _allMetals.FirstOrDefault(x => x.Id == Model.CommodityId) is { } m)
            {
                ApplyDirectionLabor(m);   // Model.PayFactor/PayCommodityId = madenin iÅŸÃ§ilik default'u
                BuildPayList();
                EnsurePayItem();
            }
            Recalc(EditedField.PaymentType);
        }
    }

    private void OnQuantityChanged(decimal value) { Model.Quantity = value; Recalc(EditedField.Amount); }
    private void OnAmountChanged(decimal value)   { Model.Amount = value;   Recalc(EditedField.Amount); }
    private void OnFactorChanged(decimal value)   { _baseFactor = value;    Recalc(EditedField.Amount); }

    // Total (HAS) elle dÃ¼zenlenince Factor'u geri-hesapla. Normal: baz milyem; PeÅŸin/Bedelli: efektif Factor.
    private void OnTotalChanged(decimal value)
    {
        if (!HasPriceMode)
        {
            _baseFactor = Model.Amount != 0m ? value / Model.Amount : _baseFactor;
            Recalc(EditedField.Amount);   // Total = Amount Ã— baseFactor = value
        }
        else
        {
            Model.Total  = value;
            Model.Factor = Model.Amount != 0m ? value / Model.Amount : Model.Factor;
            RecalcPriceLeg(value);        // Total'Ä± koru, yalnÄ±z bedeli yeniden hesapla
        }
    }

    // PeÅŸin/Bedelli: verilen Total'Ä± koruyarak bedel bacaÄŸÄ±nÄ± (parite) yeniden hesaplar.
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

    // Ortak panel stilleri (ProcessPanelStyles SSOT) â€” Metal alan bazÄ±nda farklÄ± geniÅŸlik kullanÄ±r (60/240px).
    private string GroupStyle()          => ProcessPanelStyles.Group(_isMobile);
    private string GroupStyle(int w)     => ProcessPanelStyles.Group(_isMobile, w);
    private string ControlStyle()        => ProcessPanelStyles.Control(_isMobile);

    // â”€â”€ Base kancalarÄ± (HandleSave / LoadForEditAsync iskeleti ProcessPanelHostBase'te) â”€â”€

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
        _editLoaded = true;        // OnInitialized'Ä±n default metal seÃ§imini iptal et (yarÄ±ÅŸÄ± kapat)
        await _initTask;           // veriler (Ã¶zellikle _allMetals) yÃ¼klensin â†’ m bulunur, default ezme olmaz

        var m = _allMetals.FirstOrDefault(x => x.Id == dto.CommodityId);
        if (m is { })
        {
            Model.MainUnitId  = m.FollowingUnitId;           // ana birim garanti (BuildPayList filtresi buna baÄŸlÄ±)
            _factorReadOnly   = !m.FactorChange;
            _isQuantity       = m.IsQuantity;
            _stableQuantity   = m.StableQuantity;
            _laborType        = m.LaborType;
            var inflow        = Model.Direction.IsInflow();
            _laborReadOnly    = !(inflow ? m.EntryLaborChange : m.ExitLaborChange);
            _showAdet         = m.IsQuantity || m.LaborType == MetalLaborType.Quantity;
            _amountReadOnly   = m.IsQuantity && m.StableQuantity > 0m;
        }
        _baseFactor = HasPriceMode ? (m?.Factor ?? dto.Factor) : dto.Factor;   // Normal=saved milyem; PeÅŸin/Bedelli=pÃ¼r milyem

        // TEK PAY SETÄ° (ERPPROV3) â†’ her iki mod aynÄ±: Model.Pay* zaten dto'da; ayrÄ± state reconstruction YOK.
        // Pay listesini kur + saved PayCommodityId'yi seÃ§ (aktif listede yoksa kaybolmasÄ±n diye baÅŸa ekle).
        BuildPayList();
        if (Model.PayCommodityId is { } pcid && _activePayItems.All(x => x.Id != pcid))
            _activePayItems.Insert(0, new PayComboItem(pcid, Model.PayCommodityCode ?? string.Empty, true, Model.PayUnitId, Model.PayUnitCode));
        _selectedPayItem = _activePayItems.FirstOrDefault(x => x.Id == Model.PayCommodityId);

        // Varyant seÃ§eneklerini yÃ¼kle (kayÄ±tlÄ± Model.VariantId combo'da gÃ¶rÃ¼nsÃ¼n; VariantId dto'dan zaten geldi â€” dokunma).
        if (Model.CommodityId is { } metalId)
        {
            _variantOptions = await MetalService.GetVariantPickerListAsync(metalId);
        }
    }
}

