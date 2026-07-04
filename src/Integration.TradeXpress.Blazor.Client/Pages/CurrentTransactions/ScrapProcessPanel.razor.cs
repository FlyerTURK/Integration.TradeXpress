using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Hurda (Scrap) fiş satırı paneli — ortak iskelet ProcessPanelHostBase'te; burada hurda lookup'ı,
/// Has (Miktar×Milyem) ana bacağı ve Peşin/Bedelli fiyat bacağı var.
/// </summary>
public partial class ScrapProcessPanel
{
    [Inject] private IScrapAppService ScrapService { get; set; } = default!;
    [Inject] private ICashAppService CashService { get; set; } = default!;
    [Inject] private ICurrencyUnitAppService CurrencyUnitService { get; set; } = default!;
    [Inject] private IEffectivePriceAppService PriceService { get; set; } = default!;
    [Inject] private IParityAppService ParityService { get; set; } = default!;

    protected override ProcessType ProcessType => ProcessType.Scrap;

    protected override VoucherLineDto CreateModel() => new()
    {
        Type        = ProcessType.Scrap,
        Direction   = ProcessDirectionType.Outbound,   // hurda varsayılan: çıkış
        PaymentType = ProcessPaymentType.Normal,
        Factor      = 0.570m,                           // milyem
        DueDate     = DateTime.Today,
    };

    private bool _isMobile;

    private List<ScrapListDto> _allScraps = new();
    private List<ScrapListDto> _activeScraps = new();

    private List<CurrencyUnitListDto> _allCurrencyUnits = new();
    private List<CurrencyUnitListDto> _activeUnits = new();

    private List<CashListDto> _allCashes = new();

    // Karşı bacak (Peşin→Cash, Bedelli→para birimi). Id=combo değeri, PayUnitId=gerçek para birimi (bakiye/parite).
    private record PayComboItem(Guid Id, string Code, bool IsActive, Guid? PayUnitId, string? PayUnitCode);
    private List<PayComboItem> _activePayItems = new();
    private PayComboItem? _selectedPayItem;

    private Dictionary<Guid, decimal> _buyByUnit = new();
    private Dictionary<Guid, string>  _codeByUnit = new();
    private List<(Guid Base, Guid Quote)> _parityPairs = new();

    private bool _milyemReadOnly;

    private record DirectionItem(ProcessDirectionType Value, string Label);
    private List<DirectionItem> _directionItems = new();
    private record PaymentItem(ProcessPaymentType? Value, string Label);
    private List<PaymentItem> _paymentItems = new();

    // Fiyat tipi (salt UI): kanonik PayFactor Has başına; Miktar modunda ×Factor gösterilir.
    private PayFactorType _priceType = PayFactorType.Has;
    private record PriceTypeItem(PayFactorType Value, string Label);
    private List<PriceTypeItem> _priceTypeItems = new();

    private bool HasPriceLeg =>
        Model.PaymentType is ProcessPaymentType.WithCash or ProcessPaymentType.WithCurrency;

    private decimal DisplayPayFactor =>
        _priceType == PayFactorType.Quantity ? Model.PayFactor * Model.Factor : Model.PayFactor;

    protected override async Task OnInitializedAsync()
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
        };
        _priceTypeItems = new()
        {
            new(PayFactorType.Has,      L["Enum:PayFactorType:Has"].Value),
            new(PayFactorType.Quantity, L["Enum:PayFactorType:Quantity"].Value),
        };

        _allScraps = await ScrapService.GetPickerListAsync();
        _activeScraps = _allScraps.Where(s => s.IsActive).ToList();

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

        if (_activeScraps.Count > 0)
            OnScrapChanged(_activeScraps[0].Id);
    }

    private decimal BuyOf(Guid id) => _buyByUnit.GetValueOrDefault(id);
    private Guid? ParityMainOf(Guid a, Guid b)
        => ParityResolver.ResolveBaseId(
            _parityPairs, a, b,
            id => CurrencyUnitPriority.RankOf(_codeByUnit.GetValueOrDefault(id, string.Empty)));

    // Has = Miktar × Milyem = Amount × Factor = Total (ana bacak).
    private void RecomputeHas()
    {
        Model.Total = Model.Amount * Model.Factor;
    }

    private void Recalc(EditedField edited)
    {
        Model.EditedField = edited;
        RecomputeHas();

        if (!HasPriceLeg || Model.MainUnitId == Guid.Empty || Model.PayUnitId is null)
        {
            // Fiyat bacağı yok (Normal/İade/Emanet) → yalnız Has; pay alanları sıfır.
            Model.PayFactor = 0m;
            Model.PayTotal  = 0m;
            Model.Profit    = 0m;
            return;
        }

        // Calculator'a ana bacak Has (=Total), Factor=1 verilir → PayTotal = Has × parite, Profit hesaplanır.
        var r = VoucherLineCalculator.Calculate(
            new VoucherLineCalcInput(
                ProcessType:  ProcessType.Scrap,
                Direction:    Model.Direction,
                PaymentType:  Model.PaymentType,
                MainUnitId:   Model.MainUnitId,
                PayUnitId:    Model.PayUnitId,
                Amount:       Model.Total,
                Factor:       1m,
                Total:        Model.Total,
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

    private void OnScrapChanged(Guid? id)
    {
        var s = id.HasValue ? _allScraps.FirstOrDefault(x => x.Id == id.Value) : null;
        Model.CommodityId   = id;
        Model.CommodityCode = s?.Code ?? string.Empty;
        Model.MainUnitId    = s?.FollowingUnitId ?? Guid.Empty;
        if (s is { }) { Model.Factor = s.Factor; _milyemReadOnly = !s.FactorChange; }

        BuildPayList();
        EnsurePayItem();
        Recalc(EditedField.Commodity);
    }

    // Peşin → Cash kayıtları (PayUnit = Cash.FollowingUnit); Bedelli → para birimi (ana hariç).
    private void BuildPayList()
    {
        if (Model.PaymentType == ProcessPaymentType.WithCash)
        {
            _activePayItems = _allCashes
                .Where(c => c.IsActive && c.FollowingUnitId != Model.MainUnitId)
                .Select(c => new PayComboItem(c.Id, c.Code,
                                              true,
                                              c.FollowingUnitId == Guid.Empty ? null : c.FollowingUnitId,
                                              c.FollowingUnitCode))
                .ToList();
        }
        else
        {
            _activePayItems = _activeUnits
                .Where(u => u.Id != Model.MainUnitId)
                .Select(u => new PayComboItem(u.Id, u.Code, true, u.Id, u.Code))
                .ToList();
        }
    }

    private void EnsurePayItem()
    {
        if (Model.PayCommodityId is null || _activePayItems.All(x => x.Id != Model.PayCommodityId))
            ApplyPayItem(_activePayItems.FirstOrDefault());
        else
            ApplyPayItem(_activePayItems.First(x => x.Id == Model.PayCommodityId));
    }

    private void ApplyPayItem(PayComboItem? item)
    {
        _selectedPayItem       = item;
        Model.PayCommodityId   = item?.Id;
        Model.PayCommodityCode = item?.Code;
        Model.PayUnitId        = item?.PayUnitId;
    }

    private void OnAmountChanged(decimal value) { Model.Amount = value; Recalc(EditedField.Amount); }
    private void OnFactorChanged(decimal value) { Model.Factor = value; Recalc(EditedField.Amount); }

    // Total (HAS) elle düzenlenince milyemi (Factor) geri-hesapla → Amount × Factor = value korunur.
    private void OnTotalChanged(decimal value)
    {
        Model.Factor = Model.Amount != 0m ? value / Model.Amount : Model.Factor;
        Recalc(EditedField.Amount);
    }

    private void OnDirectionChanged(ProcessDirectionType value) { Model.Direction = value; Recalc(EditedField.Direction); }

    private void OnPaymentTypeChanged(ProcessPaymentType? value)
    {
        Model.PaymentType = value;
        if (HasPriceLeg) { BuildPayList(); EnsurePayItem(); }
        Recalc(EditedField.PaymentType);
    }

    private void OnPayItemChanged(Guid? id)
    {
        ApplyPayItem(id.HasValue ? _activePayItems.FirstOrDefault(x => x.Id == id.Value) : null);
        Recalc(EditedField.PayUnit);
    }

    // Ekrandaki Fiyat → kanonik (Has başına) PayFactor'a çevir.
    private void OnDisplayPayFactorChanged(decimal value)
    {
        Model.PayFactor = _priceType == PayFactorType.Quantity
            ? (Model.Factor != 0m ? value / Model.Factor : value)
            : value;
        Recalc(EditedField.PayFactor);
    }

    private void OnPayTotalChanged(decimal value)  { Model.PayTotal = value; Recalc(EditedField.PayTotal); }
    private void OnPriceTypeChanged(PayFactorType value) => _priceType = value;

    // Ortak panel stilleri (ProcessPanelStyles SSOT).
    private string GroupStyle()   => ProcessPanelStyles.Group(_isMobile);
    private string ControlStyle() => ProcessPanelStyles.Control(_isMobile);

    // ── Base kancaları (HandleSave / LoadForEditAsync iskeleti ProcessPanelHostBase'te) ──

    protected override bool CanSave() => Model.CommodityId is not null && Model.MainUnitId != Guid.Empty;

    protected override void PrepareModelForSave()
    {
        RecomputeHas();

        if (!HasPriceLeg)
        {
            // Fiyat bacağı yok → pay alanları temizlenir.
            Model.PayCommodityId   = null;
            Model.PayCommodityCode = null;
            Model.PayUnitId   = null;
            Model.PayFactor   = 0m;
            Model.PayTotal    = 0m;
            Model.Profit      = 0m;
            Model.PayUnitRate = 0m;
        }
        else
        {
            Model.PayUnitRate = Model.PayUnitId is { } pu ? BuyOf(pu) : 0m;
        }
    }

    protected override void OnAfterSavePersisted()
    {
        Model.EditedField = EditedField.None;
    }

    protected override void ResetVolatileFields()
    {
        Model.Amount = 0m;
        RecomputeHas();
        Model.PayTotal    = 0m;
        Model.Description = null;
    }

    protected override Task OnLoadedForEditAsync(VoucherLineDto dto)
    {
        var scrap = _allScraps.FirstOrDefault(x => x.Id == dto.CommodityId);
        _milyemReadOnly = scrap is { } ? !scrap.FactorChange : false;
        if (HasPriceLeg) { BuildPayList(); EnsurePayItem(); }
        return Task.CompletedTask;
    }
}
