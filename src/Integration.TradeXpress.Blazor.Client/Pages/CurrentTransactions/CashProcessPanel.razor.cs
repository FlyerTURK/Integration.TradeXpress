using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Nakit (Cash) fiş satırı paneli — ortak parametre seti / HandleSave iskeleti / LoadForEditAsync
/// deseni ProcessPanelHostBase'te; burada yalnız Cash'e özel lookup + hesap motoru köprüsü var.
/// </summary>
public partial class CashProcessPanel
{
    [Inject] private ICashAppService CashService { get; set; } = default!;
    [Inject] private ICurrencyUnitAppService CurrencyUnitService { get; set; } = default!;
    [Inject] private IEffectivePriceAppService PriceService { get; set; } = default!;
    [Inject] private IParityAppService ParityService { get; set; } = default!;

    protected override ProcessType ProcessType => ProcessType.Cash;

    protected override VoucherLineDto CreateModel() => new()
    {
        Type        = ProcessType.Cash,
        Direction   = ProcessDirectionType.Outbound,
        PaymentType = ProcessPaymentType.Normal,
        Factor      = 1m,
    };

    // ── UI / lookup altyapısı (edit değeri değil) ──
    private bool _isMobile;
    private bool _payFactorReadOnly;
    private bool _payTotalReadOnly;

    private List<CashListDto>         _allCashes        = new();
    private List<CurrencyUnitListDto> _allCurrencyUnits = new();
    private List<CashListDto>         _activeCashes     = new();
    private CashListDto?              _selectedCash;

    private record PayComboItem(Guid Id, string Code, string Name, bool IsActive, Guid? PayUnitId, string? PayUnitCode);
    private List<PayComboItem> _allPayItems    = new();
    private List<PayComboItem> _activePayItems = new();
    private PayComboItem?      _selectedPayItem;

    private record DirectionItem(ProcessDirectionType Value, string Label);
    private record PaymentItem(ProcessPaymentType Value, string Label);
    private List<DirectionItem> _directionItems = new();
    private List<PaymentItem>   _paymentItems   = new();

    // Kur/parite (in-process hesap için)
    private Dictionary<Guid, decimal> _buyByUnit  = new();
    private Dictionary<Guid, string>  _codeByUnit = new();
    private List<(Guid Base, Guid Quote)> _parityPairs = new();

    protected override async Task OnInitializedAsync()
    {
        _directionItems = new()
        {
            new(ProcessDirectionType.Inbound,  L["UI:ProcessDirectionType:Inbound"].Value),
            new(ProcessDirectionType.Outbound, L["UI:ProcessDirectionType:Outbound"].Value),
        };
        _paymentItems = new()
        {
            new(ProcessPaymentType.Normal,   L["UI:ProcessPaymentType:Normal"].Value),
            new(ProcessPaymentType.WithCash, L["UI:ProcessPaymentType:WithCash"].Value),
        };

        var cashResult = await CashService.GetListAsync(new CashListRequestDto { MaxResultCount = 1000 });
        _allCashes    = cashResult.Items.ToList();
        _activeCashes = _allCashes.Where(c => c.IsActive).ToList();

        var unitResult = await CurrencyUnitService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _allCurrencyUnits = unitResult.Items.ToList();
        _codeByUnit = _allCurrencyUnits.ToDictionary(u => u.Id, u => u.Code);

        var prices = await PriceService.GetCurrentPricesAsync();
        _buyByUnit = prices.ToDictionary(p => p.Id, p => p.Buy);
        var parityResult = await ParityService.GetListAsync(new ParityListRequestDto { MaxResultCount = 1000 });
        _parityPairs = parityResult.Items.Select(p => (p.BaseCurrencyUnitId, p.QuoteCurrencyUnitId)).ToList();

        if (_activeCashes.Count > 0)
            await OnCommodityChanged(_activeCashes[0].Id);

        BuildPayList();
        await SelectFirstPayItem();
    }

    // ── Hesap motoru köprüsü (in-process) ───────────────────────────────────────

    private decimal BuyOf(Guid id) => _buyByUnit.GetValueOrDefault(id);

    private Guid? ParityMainOf(Guid a, Guid b)
        => ParityResolver.ResolveBaseId(
            _parityPairs, a, b,
            id => CurrencyUnitPriority.RankOf(_codeByUnit.GetValueOrDefault(id, string.Empty)));

    /// <summary>Hesap motorunu çağırır; model'in Fiyat/Tutar/Piyasa/Kâr + readonly kilitlerini günceller.</summary>
    private void Recalc(EditedField edited)
    {
        Model.EditedField = edited;   // save'de sunucuya gönderilir (recompute yönü)

        if (Model.MainUnitId == Guid.Empty || Model.PayUnitId is null)
            return;

        var r = VoucherLineCalculator.Calculate(
            new VoucherLineCalcInput(
                ProcessType:  ProcessType.Cash,
                Direction:    Model.Direction,
                PaymentType:  Model.PaymentType,
                MainUnitId:   Model.MainUnitId,
                PayUnitId:    Model.PayUnitId,
                Amount:       Model.Amount,
                Factor:       1m,
                Total:        Model.Amount,
                PayFactor:    Model.PayFactor,
                PayTotal:     Model.PayTotal,
                MarketPrice:  Model.MarketPrice,
                EditedField:  edited),
            BuyOf,
            ParityMainOf);

        Model.Amount      = r.Amount;   // PayTotal girilip Miktar boşsa türetilen MİKTAR geri yansır
        Model.PayFactor   = r.PayFactor;
        Model.PayTotal    = r.PayTotal;
        Model.MarketPrice = r.MarketPrice;
        Model.Profit      = r.Profit;
        Model.Factor      = r.Factor;
        Model.Total       = r.Total;
        _payFactorReadOnly = r.PayFactorReadOnly;
        _payTotalReadOnly  = r.PayTotalReadOnly;
    }

    private async Task OnPaymentTypeChanged(ProcessPaymentType? value)
    {
        Model.PaymentType    = value;
        Model.PayCommodityId = null;
        Model.PayUnitId      = null;
        _selectedPayItem     = null;
        BuildPayList();
        await SelectFirstPayItem();
    }

    private void BuildPayList()
    {
        if (Model.PaymentType == ProcessPaymentType.WithCash)
        {
            // Peşin: karşı liste de Cash; seçili emtia kendisiyle ödenemez → dışla.
            _allPayItems = _allCashes
                .Where(c => c.Id != Model.CommodityId)
                .Select(c => new PayComboItem(c.Id, c.Code, c.Name ?? string.Empty, c.IsActive,
                                              c.FollowingUnitId == Guid.Empty ? null : c.FollowingUnitId,
                                              c.FollowingUnitCode))
                .ToList();
        }
        else
        {
            _allPayItems = _allCurrencyUnits
                .Select(u => new PayComboItem(u.Id, u.Code, u.Name, u.IsActive, u.Id, u.Code))
                .ToList();
        }

        _activePayItems = _allPayItems.Where(p => p.IsActive).ToList();
    }

    private async Task SelectFirstPayItem()
    {
        if (_activePayItems.Count == 0)
            return;

        // Varsayılan: karşı birimi takip birimiyle (MainUnitId) aynı olan kayıt → aynı birim (Fiyat=1/Tutar=Miktar kilitli).
        var match = Model.MainUnitId != Guid.Empty
            ? _activePayItems.FirstOrDefault(p => p.PayUnitId == Model.MainUnitId)
            : null;
        await OnPayCommodityChanged((match ?? _activePayItems[0]).Id);
    }

    private Task OnPayCommodityChanged(Guid? id)
    {
        Model.PayCommodityId = id;
        _selectedPayItem     = id.HasValue ? _allPayItems.FirstOrDefault(p => p.Id == id.Value) : null;
        Model.PayCommodityCode = _selectedPayItem?.Code;

        // Peşin: PayUnit = Cash'in FollowingUnit'i; Normal: PayUnit = birimin kendisi.
        Model.PayUnitId = Model.PaymentType == ProcessPaymentType.WithCash
            ? _selectedPayItem?.PayUnitId
            : _selectedPayItem?.Id;

        Recalc(EditedField.PayUnit);
        return Task.CompletedTask;
    }

    private async Task OnCommodityChanged(Guid? id)
    {
        Model.CommodityId   = id;
        _selectedCash       = id.HasValue ? _allCashes.FirstOrDefault(c => c.Id == id.Value) : null;
        Model.CommodityCode = _selectedCash?.Code ?? string.Empty;
        Model.MainUnitId    = (_selectedCash?.FollowingUnitId is { } f && f != Guid.Empty) ? f : Guid.Empty;

        if (Model.PaymentType == ProcessPaymentType.WithCash)
        {
            // Peşin: karşı liste yeni emtiayı dışlamalı + seçim geçerli kalmalı (recalc içeride).
            BuildPayList();
            await SelectFirstPayItem();
        }
        else
        {
            Recalc(EditedField.Commodity);
        }
    }

    private Task OnDirectionChanged(ProcessDirectionType value)
    {
        Model.Direction = value;
        Recalc(EditedField.Direction);
        return Task.CompletedTask;
    }

    private Task OnTotalChanged(decimal value)
    {
        Model.Amount = value;
        Recalc(EditedField.Amount);
        return Task.CompletedTask;
    }

    private Task OnPayFactorChanged(decimal value)
    {
        Model.PayFactor = value;
        Recalc(EditedField.PayFactor);
        return Task.CompletedTask;
    }

    private Task OnPayTotalChanged(decimal value)
    {
        Model.PayTotal = value;
        Recalc(EditedField.PayTotal);
        return Task.CompletedTask;
    }

    // Ortak panel stilleri (ProcessPanelStyles SSOT) — markup kısa çağrı kullansın diye sarmalayıcı.
    private string GroupStyle()   => ProcessPanelStyles.Group(_isMobile);
    private string ControlStyle() => ProcessPanelStyles.Control(_isMobile);

    // ── Base kancaları (HandleSave / LoadForEditAsync iskeleti ProcessPanelHostBase'te) ──

    protected override bool CanSave() => Model.MainUnitId != Guid.Empty; // emtia/birim seçili değilse çık

    /// <summary>Türetilen değerler (WYSIWYG: Piyasa = pay birim kuru).</summary>
    protected override void PrepareModelForSave()
    {
        Model.Factor      = 1m;
        Model.Total       = Model.Amount;
        Model.MarketPrice = Model.PayUnitId is { } mpu ? BuyOf(mpu) : Model.MarketPrice;
        Model.PayUnitRate = Model.PayUnitId is { } pu ? BuyOf(pu) : 0m;
    }

    protected override void OnAfterSavePersisted()
    {
        Model.EditedField = EditedField.None;
    }

    /// <summary>Yeni ekleme: bir sonraki satır için tutarları sıfırla (sınıflandırma/seçimler kalsın).</summary>
    protected override void ResetVolatileFields()
    {
        Model.Amount      = 0m;
        Model.PayTotal    = 0m;
        Model.Profit      = 0m;
        Model.Description = null;
    }

    protected override Task OnLoadedForEditAsync(VoucherLineDto dto)
    {
        // Değişmeden kaydedilirse saklı Tutar korunsun (sunucu PayTotal'i ezmesin).
        Model.EditedField = EditedField.PayTotal;

        _selectedCash    = dto.CommodityId is { } cid ? _allCashes.FirstOrDefault(c => c.Id == cid) : null;
        BuildPayList();
        _selectedPayItem = dto.PayCommodityId is { } pid ? _allPayItems.FirstOrDefault(p => p.Id == pid) : null;

        var sameUnit = Model.MainUnitId != Guid.Empty && Model.PayUnitId is { } pu && Model.MainUnitId == pu;
        _payFactorReadOnly = sameUnit;
        _payTotalReadOnly  = sameUnit;

        return Task.CompletedTask;
    }
}

