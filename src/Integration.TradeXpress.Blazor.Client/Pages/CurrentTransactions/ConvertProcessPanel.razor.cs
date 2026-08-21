using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Çevir (Convert) fiş satırı paneli — ortak taban ProcessPanelHostBase'te; burada birim çifti seçimi,
/// bakiyeden Amount auto-fill ve parite hesabı var.
/// </summary>
public partial class ConvertProcessPanel
{
    [Inject] private ICurrencyUnitAppService CurrencyUnitService { get; set; } = default!;
    [Inject] private IEffectivePriceAppService PriceService { get; set; } = default!;
    [Inject] private IParityAppService ParityService { get; set; } = default!;

    protected override ProcessType ProcessType => ProcessType.Convert;

    protected override VoucherLineDto CreateModel() => new()
    {
        Type        = ProcessType.Convert,
        Direction   = ProcessDirectionType.Credit,
        PaymentType = null,
        Factor      = 1m,
    };

    private bool _isMobile;

    private List<CurrencyUnitListDto> _allCurrencyUnits = new();
    private List<CurrencyUnitListDto> _activeUnits = new();
    private List<CurrencyUnitListDto> _counterUnits = new();   // ana hariç

    private Dictionary<Guid, decimal> _buyByUnit = new();
    private Dictionary<Guid, string>  _codeByUnit = new();
    private List<(Guid Base, Guid Quote)> _parityPairs = new();

    private Dictionary<Guid, decimal> _netByUnit = new();   // cari bakiyesi: birim → net (>0 alacak, <0 borç)

    private record DirectionItem(ProcessDirectionType Value, string Label);
    private List<DirectionItem> _directionItems = new();

    protected override async Task OnInitializedAsync()
    {
        _directionItems = new()
        {
            new(ProcessDirectionType.Credit, L["Enum:ProcessDirectionType:Credit"].Value),
            new(ProcessDirectionType.Debit,  L["Enum:ProcessDirectionType:Debit"].Value),
        };

        var unitResult = await CurrencyUnitService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _allCurrencyUnits = unitResult.Items.ToList();
        _activeUnits = _allCurrencyUnits.Where(u => u.IsActive).ToList();
        _codeByUnit = _allCurrencyUnits.ToDictionary(u => u.Id, u => u.Code);

        var prices = await PriceService.GetCurrentPricesAsync();
        _buyByUnit = prices.ToDictionary(p => p.Id, p => p.Buy);
        var parityResult = await ParityService.GetListAsync(new ParityListRequestDto { MaxResultCount = 1000 });
        _parityPairs = parityResult.Items.Select(p => (p.BaseCurrencyUnitId, p.QuoteCurrencyUnitId)).ToList();

        await LoadBalancesAsync();

        if (_activeUnits.Count > 0)
            OnMainUnitChanged(_activeUnits[0].Id);
    }

    private async Task LoadBalancesAsync()
    {
        if (Context.SubAccountId is { } sa)
        {
            var bal = await VoucherService.GetBalancesAsync(sa);
            _netByUnit = bal.Lines.ToDictionary(l => l.UnitId, l => l.Net);
        }
    }

    /// <summary>Seçili ana birimde, seçili yönde bakiye varsa Amount'u otomatik doldurur (ERPPROV3 paritesi).</summary>
    private void AutoFillAmountFromBalance()
    {
        if (Model.MainUnitId == Guid.Empty) return;
        var net = _netByUnit.GetValueOrDefault(Model.MainUnitId);
        var auto = Model.Direction == ProcessDirectionType.Debit
            ? (net < 0 ? -net : 0m)   // borç bakiyesi
            : (net > 0 ? net : 0m);   // alacak bakiyesi
        if (auto > 0m) Model.Amount = auto;
    }

    // ── hesap motoru yardımcıları ──
    private decimal BuyOf(Guid id) => _buyByUnit.GetValueOrDefault(id);
    private Guid? ParityMainOf(Guid a, Guid b)
        => ParityResolver.ResolveBaseId(
            _parityPairs, a, b,
            id => CurrencyUnitPriority.RankOf(_codeByUnit.GetValueOrDefault(id, string.Empty)));

    private void Recalc(EditedField edited)
    {
        Model.EditedField = edited;
        if (Model.MainUnitId == Guid.Empty || Model.PayUnitId is null)
            return;

        var r = VoucherLineCalculator.Calculate(
            new VoucherLineCalcInput(
                ProcessType:  ProcessType.Convert,
                Direction:    Model.Direction,
                PaymentType:  null,
                MainUnitId:   Model.MainUnitId,
                PayUnitId:    Model.PayUnitId,
                Amount:       Model.Amount,
                Factor:       1m,
                Total:        Model.Amount,
                PayFactor:    Model.PayFactor,
                PayTotal:     Model.PayTotal,
                MarketPrice:  Model.MarketPrice,
                EditedField:  edited),
            BuyOf, ParityMainOf);

        Model.Amount      = r.Amount;
        Model.PayFactor   = r.PayFactor;
        Model.PayTotal    = r.PayTotal;
        Model.MarketPrice = r.MarketPrice;
        Model.Factor      = r.Factor;
        Model.Total       = r.Total;
    }

    private void OnMainUnitChanged(Guid? id)
    {
        var u = id.HasValue ? _allCurrencyUnits.FirstOrDefault(x => x.Id == id.Value) : null;
        Model.CommodityId   = id;
        Model.CommodityCode = u?.Code ?? string.Empty;
        Model.MainUnitId    = id ?? Guid.Empty;

        // Karşı liste = ana hariç. Karşı seçim geçersiz/boş ise ilk farklı birime ayarla
        // (ana = karşı olamaz; bu yüzden boş kalmasın).
        _counterUnits = _activeUnits.Where(x => x.Id != id).ToList();
        if (Model.PayCommodityId is null || _counterUnits.All(x => x.Id != Model.PayCommodityId))
        {
            var c = _counterUnits.FirstOrDefault();
            Model.PayCommodityId   = c?.Id;
            Model.PayCommodityCode = c?.Code;
            Model.PayUnitId        = c?.Id;
        }

        AutoFillAmountFromBalance();
        Recalc(EditedField.Commodity);
    }

    private void OnCounterUnitChanged(Guid? id)
    {
        var u = id.HasValue ? _allCurrencyUnits.FirstOrDefault(x => x.Id == id.Value) : null;
        Model.PayCommodityId   = id;
        Model.PayCommodityCode = u?.Code;
        Model.PayUnitId        = id;
        Recalc(EditedField.PayUnit);
    }

    private void OnDirectionChanged(ProcessDirectionType value) { Model.Direction = value; AutoFillAmountFromBalance(); Recalc(EditedField.Direction); }
    private void OnAmountChanged(decimal value)    { Model.Amount = value;    Recalc(EditedField.Amount); }
    private void OnPayFactorChanged(decimal value) { Model.PayFactor = value; Recalc(EditedField.PayFactor); }
    private void OnPayTotalChanged(decimal value)  { Model.PayTotal = value;  Recalc(EditedField.PayTotal); }

    // Ortak panel stilleri (ProcessPanelStyles SSOT).
    private string GroupStyle()   => ProcessPanelStyles.Group(_isMobile);
    private string ControlStyle() => ProcessPanelStyles.Control(_isMobile);

    // ── Base override'ları (HandleSave / LoadForEditAsync ortak akışı ProcessPanelHostBase'te) ──

    protected override bool CanSave()
        => Model.MainUnitId != Guid.Empty && Model.PayUnitId is not null; // ana/karşı birim seçili değilse çık

    protected override void PrepareModelForSave()
    {
        Model.PaymentType = null;
        Model.Factor      = 1m;
        Model.Total       = Model.Amount;
        Model.Profit      = 0m;          // Çevir'de kâr anlamsız
        Model.PayUnitRate = Model.PayUnitId is { } pu ? BuyOf(pu) : 0m;
    }

    protected override void OnAfterSavePersisted()
    {
        Model.EditedField = EditedField.None;
    }

    protected override void ResetVolatileFields()
    {
        Model.Amount      = 0m;
        Model.PayTotal    = 0m;
        Model.Description = null;
    }

    /// <summary>Sonraki satır auto-fill'i güncel bakiyeyle çalışsın.</summary>
    protected override Task OnAfterResetAsync() => LoadBalancesAsync();

    protected override Task OnLoadedForEditAsync(VoucherLineDto dto)
    {
        _counterUnits = _activeUnits.Where(x => x.Id != dto.CommodityId).ToList();
        return Task.CompletedTask;
    }
}
