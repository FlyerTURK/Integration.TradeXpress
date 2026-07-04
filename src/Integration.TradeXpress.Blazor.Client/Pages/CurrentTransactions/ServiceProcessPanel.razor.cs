using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Hizmet (Service) fiş satırı paneli — ortak iskelet ProcessPanelHostBase'te; burada hizmet lookup'ı
/// ve tek-değerli tutar (PayFactor=PayTotal) mantığı var. Ana bacak boştur (tutar pay-leg'de).
/// </summary>
public partial class ServiceProcessPanel
{
    [Inject] private IServiceAppService ServiceService { get; set; } = default!;
    [Inject] private ICashAppService CashService { get; set; } = default!;
    [Inject] private ICurrencyUnitAppService CurrencyUnitService { get; set; } = default!;

    protected override ProcessType ProcessType => ProcessType.Service;

    protected override VoucherLineDto CreateModel() => new()
    {
        Type        = ProcessType.Service,
        Direction   = ProcessDirectionType.Outbound,
        PaymentType = ProcessPaymentType.Normal,
        Factor      = 1m,
    };

    private bool _isMobile;

    // Hizmet listesi
    private List<ServiceListDto> _allServices = new();
    private List<ServiceListDto> _activeServices = new();
    private ServiceListDto? _selectedService;

    // Birim seçimi (Normal: CurrencyUnit, Peşin: Cash) → MainUnitId çözülür
    // UnitItem.PayUnitId = çözülen para birimi (Normal: birim, Peşin: kasanın FollowingUnit'i)
    private record UnitItem(Guid Id, string Code, bool IsActive, Guid PayUnitId);
    private List<CashListDto> _allCashes = new();
    private List<CurrencyUnitListDto> _allCurrencyUnits = new();
    private List<UnitItem> _activeUnitItems = new();

    private record DirectionItem(ProcessDirectionType Value, string Label);
    private record PaymentItem(ProcessPaymentType Value, string Label);
    private List<DirectionItem> _directionItems = new();
    private List<PaymentItem> _paymentItems = new();

    protected override async Task OnInitializedAsync()
    {
        _directionItems = new()
        {
            new(ProcessDirectionType.Inbound,  L["Enum:ProcessDirectionType:Inbound"].Value),
            new(ProcessDirectionType.Outbound, L["Enum:ProcessDirectionType:Outbound"].Value),
        };
        _paymentItems = new()
        {
            new(ProcessPaymentType.Normal,   L["Enum:ProcessPaymentType:Normal"].Value),
            new(ProcessPaymentType.WithCash, L["Enum:ProcessPaymentType:WithCash"].Value),
        };

        var svc = await ServiceService.GetPickerListAsync();
        _allServices = svc;
        _activeServices = svc.Where(s => s.IsActive).ToList();

        var cashResult = await CashService.GetListAsync(new CashListRequestDto { MaxResultCount = 1000 });
        _allCashes = cashResult.Items.ToList();
        var unitResult = await CurrencyUnitService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _allCurrencyUnits = unitResult.Items.ToList();

        if (_activeServices.Count > 0)
            OnServiceChanged(_activeServices[0].Id);

        BuildUnitList();
        SelectFirstUnit();
    }

    private void OnServiceChanged(Guid? id)
    {
        Model.CommodityId   = id;
        _selectedService    = id.HasValue ? _allServices.FirstOrDefault(s => s.Id == id.Value) : null;
        Model.CommodityCode = _selectedService?.Code ?? string.Empty;
    }

    private void OnAmountChanged(decimal value)
    {
        // Eski Amount/Total → burada PayFactor/PayTotal (eşit, tek değer).
        Model.PayFactor = value;
        Model.PayTotal  = value;
    }

    private void OnPaymentTypeChanged(ProcessPaymentType? value)
    {
        Model.PaymentType    = value;
        Model.PayCommodityId = null;
        Model.PayUnitId      = null;
        BuildUnitList();
        SelectFirstUnit();
    }

    private void BuildUnitList()
    {
        if (Model.PaymentType == ProcessPaymentType.WithCash)
        {
            _activeUnitItems = _allCashes
                .Where(c => c.IsActive)
                .Select(c => new UnitItem(c.Id, c.Code, c.IsActive,
                                          c.FollowingUnitId == Guid.Empty ? Guid.Empty : c.FollowingUnitId))
                .ToList();
        }
        else
        {
            _activeUnitItems = _allCurrencyUnits
                .Where(u => u.IsActive)
                .Select(u => new UnitItem(u.Id, u.Code, u.IsActive, u.Id))
                .ToList();
        }
    }

    private void SelectFirstUnit()
    {
        if (_activeUnitItems.Count > 0)
            OnUnitChanged(_activeUnitItems[0].Id);
    }

    private void OnUnitChanged(Guid? id)
    {
        var item = id.HasValue ? _activeUnitItems.FirstOrDefault(u => u.Id == id.Value) : null;
        Model.PayCommodityId   = id;                                   // seçili karşılık (birim/kasa)
        Model.PayCommodityCode = item?.Code;
        Model.PayUnitId        = item is { } it && it.PayUnitId != Guid.Empty ? it.PayUnitId : null;
    }

    // Ortak panel stilleri (ProcessPanelStyles SSOT).
    private string GroupStyle()   => ProcessPanelStyles.Group(_isMobile);
    private string ControlStyle() => ProcessPanelStyles.Control(_isMobile);

    // ── Base kancaları (HandleSave / LoadForEditAsync iskeleti ProcessPanelHostBase'te) ──

    protected override bool CanSave()
        => Model.CommodityId is not null && Model.PayUnitId is not null; // hizmet/birim seçili değilse çık

    protected override void PrepareModelForSave()
    {
        // Ana bacak boş (hizmet = Commodity; tutar pay-leg'de).
        Model.MainUnitId = Guid.Empty;
        Model.Amount     = 0m;
        Model.Factor     = 0m;
        Model.Total      = 0m;
        Model.Profit     = 0m;
        // PayFactor/PayTotal OnAmountChanged'de set edildi; PayCommodity/PayUnit OnUnitChanged'de.
    }

    protected override void ResetVolatileFields()
    {
        Model.PayFactor   = 0m;
        Model.PayTotal    = 0m;
        Model.Description = null;
    }

    protected override Task OnLoadedForEditAsync(VoucherLineDto dto)
    {
        _selectedService = dto.CommodityId is { } cid ? _allServices.FirstOrDefault(s => s.Id == cid) : null;

        // Karşılık seçimi doğrudan dto.PayCommodityId (combo Value buna bağlı).
        BuildUnitList();
        return Task.CompletedTask;
    }
}
