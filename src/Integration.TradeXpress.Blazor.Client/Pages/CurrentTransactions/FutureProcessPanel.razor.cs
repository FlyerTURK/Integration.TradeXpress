using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Vadeli (Future) fiş satırı paneli — ortak taban ProcessPanelHostBase'te; burada vadeli enstrüman
/// lookup'ı, sabit Çarpan/Total ve Gram/Ons fiyat gösterimi var.
/// </summary>
public partial class FutureProcessPanel
{
    [Inject] private IFutureAppService FutureService { get; set; } = default!;
    [Inject] private ICurrencyUnitAppService CurrencyUnitService { get; set; } = default!;
    [Inject] private IEffectivePriceAppService PriceService { get; set; } = default!;
    [Inject] private IParityAppService ParityService { get; set; } = default!;

    protected override ProcessType ProcessType => ProcessType.Future;

    protected override VoucherLineDto CreateModel() => new()
    {
        Type        = ProcessType.Future,
        Direction   = ProcessDirectionType.Buy,
        PaymentType = null,
        Factor      = 1m,
        DueDate     = BusinessClock.Today(),
    };

    private bool _isMobile;

    private List<FutureListDto> _allFutures = new();
    private List<FutureListDto> _activeFutures = new();

    private List<CurrencyUnitListDto> _allCurrencyUnits = new();
    private List<CurrencyUnitListDto> _activeUnits = new();
    private List<CurrencyUnitListDto> _payUnits = new();   // ana (FollowingUnit) hariç

    private Dictionary<Guid, decimal> _buyByUnit = new();
    private Dictionary<Guid, string>  _codeByUnit = new();
    private List<(Guid Base, Guid Quote)> _parityPairs = new();

    private record DirectionItem(ProcessDirectionType Value, string Label);
    private List<DirectionItem> _directionItems = new();

    // Fiyat tipi (salt UI): kanonik PayFactor gram bazında saklanır; Ons modunda ×OunceGrams gösterilir.
    private const decimal OunceGrams = 31.1035m;
    private PayFactorType _priceType = PayFactorType.Gram;
    private record PriceTypeItem(PayFactorType Value, string Label);
    private List<PriceTypeItem> _priceTypeItems = new();

    private decimal DisplayPayFactor => _priceType == PayFactorType.Ounce ? Model.PayFactor * OunceGrams : Model.PayFactor;

    protected override async Task OnInitializedAsync()
    {
        _directionItems = new()
        {
            new(ProcessDirectionType.Buy,  L["Enum:ProcessDirectionType:Buy"].Value),
            new(ProcessDirectionType.Sell, L["Enum:ProcessDirectionType:Sell"].Value),
        };
        _priceTypeItems = new()
        {
            new(PayFactorType.Gram,  L["Enum:PayFactorType:Gram"].Value),
            new(PayFactorType.Ounce, L["Enum:PayFactorType:Ounce"].Value),
        };

        _allFutures = await FutureService.GetPickerListAsync();
        _activeFutures = _allFutures.Where(f => f.IsActive).ToList();

        var unitResult = await CurrencyUnitService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _allCurrencyUnits = unitResult.Items.ToList();
        _activeUnits = _allCurrencyUnits.Where(u => u.IsActive).ToList();
        _codeByUnit = _allCurrencyUnits.ToDictionary(u => u.Id, u => u.Code);

        var prices = await PriceService.GetCurrentPricesAsync();
        _buyByUnit = prices.ToDictionary(p => p.Id, p => p.Buy);
        var parityResult = await ParityService.GetListAsync(new ParityListRequestDto { MaxResultCount = 1000 });
        _parityPairs = parityResult.Items.Select(p => (p.BaseCurrencyUnitId, p.QuoteCurrencyUnitId)).ToList();

        if (_activeFutures.Count > 0)
            OnFutureChanged(_activeFutures[0].Id);
    }

    private decimal BuyOf(Guid id) => _buyByUnit.GetValueOrDefault(id);
    private Guid? ParityMainOf(Guid a, Guid b)
        => ParityResolver.ResolveBaseId(
            _parityPairs, a, b,
            id => CurrencyUnitPriority.RankOf(_codeByUnit.GetValueOrDefault(id, string.Empty)));

    private void RecomputeTotal() => Model.Total = Model.Amount * Model.Factor;

    private void Recalc(EditedField edited)
    {
        Model.EditedField = edited;
        if (Model.MainUnitId == Guid.Empty || Model.PayUnitId is null)
            return;

        // Calculator'a ana leg Total'ı (Miktar×Çarpan) Amount olarak verilir, Factor=1.
        var r = VoucherLineCalculator.Calculate(
            new VoucherLineCalcInput(
                ProcessType:  ProcessType.Future,
                Direction:    Model.Direction,
                PaymentType:  null,
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
    }

    private void OnFutureChanged(Guid? id)
    {
        var f = id.HasValue ? _allFutures.FirstOrDefault(x => x.Id == id.Value) : null;
        Model.CommodityId   = id;
        Model.CommodityCode = f?.Code ?? string.Empty;
        Model.MainUnitId    = f?.FollowingUnitId ?? Guid.Empty;
        Model.Factor        = f is { } ? f.FollowingFactor : 1m;
        RecomputeTotal();

        // Karşı liste = ana (FollowingUnit) hariç; seçim geçersizse ilk farklı birime ayarla.
        _payUnits = _activeUnits.Where(x => x.Id != Model.MainUnitId).ToList();
        if (Model.PayCommodityId is null || _payUnits.All(x => x.Id != Model.PayCommodityId))
        {
            var c = _payUnits.FirstOrDefault();
            Model.PayCommodityId   = c?.Id;
            Model.PayCommodityCode = c?.Code;
            Model.PayUnitId        = c?.Id;
        }

        Recalc(EditedField.Commodity);
    }

    private void OnAmountChanged(decimal value)
    {
        Model.Amount = value;
        RecomputeTotal();
        Recalc(EditedField.Amount);
    }

    private void OnPayUnitChanged(Guid? id)
    {
        var u = id.HasValue ? _allCurrencyUnits.FirstOrDefault(x => x.Id == id.Value) : null;
        Model.PayCommodityId   = id;
        Model.PayCommodityCode = u?.Code;
        Model.PayUnitId        = id;
        Recalc(EditedField.PayUnit);
    }

    private void OnDirectionChanged(ProcessDirectionType value) { Model.Direction = value; Recalc(EditedField.Direction); }
    private void OnPayTotalChanged(decimal value)  { Model.PayTotal = value;  Recalc(EditedField.PayTotal); }

    // Ekrandaki Fiyat → kanonik (gram) PayFactor'a çevir, sonra hesapla.
    private void OnDisplayPayFactorChanged(decimal value)
    {
        Model.PayFactor = _priceType == PayFactorType.Ounce ? (OunceGrams != 0m ? value / OunceGrams : value) : value;
        Recalc(EditedField.PayFactor);
    }

    // Gram/Ons değişimi yalnız gösterimi etkiler (kanonik değişmez) → recalc gerekmez.
    private void OnPriceTypeChanged(PayFactorType value) => _priceType = value;

    // Ortak panel stilleri (ProcessPanelStyles SSOT).
    private string GroupStyle()   => ProcessPanelStyles.Group(_isMobile);
    private string ControlStyle() => ProcessPanelStyles.Control(_isMobile);

    // ── Base override'ları (HandleSave / LoadForEditAsync ortak akışı ProcessPanelHostBase'te) ──

    protected override bool CanSave()
        => Model.CommodityId is not null && Model.MainUnitId != Guid.Empty && Model.PayUnitId is not null;

    protected override void PrepareModelForSave()
    {
        Model.PaymentType = null;
        RecomputeTotal();
        Model.Profit      = 0m;
        Model.PayUnitRate = Model.PayUnitId is { } pu ? BuyOf(pu) : 0m;
    }

    protected override void OnAfterSavePersisted()
    {
        Model.EditedField = EditedField.None;
    }

    protected override void ResetVolatileFields()
    {
        Model.Amount = 0m;
        RecomputeTotal();
        Model.PayTotal    = 0m;
        Model.Description = null;
    }

    protected override Task OnLoadedForEditAsync(VoucherLineDto dto)
    {
        _payUnits = _activeUnits.Where(x => x.Id != dto.MainUnitId).ToList();
        return Task.CompletedTask;
    }
}
