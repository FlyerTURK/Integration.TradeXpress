using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Fiyatlı-emtia fiş satırı panellerinin (Taş/Mücevher) ortak gövdesi — markup + TÜM davranış burada.
/// Türeyen sınıf markup'sızdır (BuildRenderTree devralınır); yalnız işlem tipini, panel başlık anahtarını
/// ve emtia picker servisini sağlar.
/// </summary>
public abstract partial class CommodityProcessPanelBase<TListDto> : IVoucherLineEditPanel where TListDto : class, IPricedCommodityListDto
{
    [Inject] protected IStringLocalizer<TradeXpressResource> L { get; set; } = default!;
    [Inject] private ICashAppService CashService { get; set; } = default!;
    [Inject] private ICurrencyUnitAppService CurrencyUnitService { get; set; } = default!;
    [Inject] private IVoucherAppService VoucherService { get; set; } = default!;
    [Inject] private IWorkingContextService Working { get; set; } = default!;
    [Inject] private IUiInteractionService Ui { get; set; } = default!;

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

    // ── Türeyen sınıfın sağladıkları ──

    /// <summary>Satırın işlem tipi (Stone/Jewelry).</summary>
    protected abstract ProcessType ProcessType { get; }

    /// <summary>Panel başlığının lokalizasyon anahtarı ("Stone"/"Jewelry").</summary>
    protected abstract string ProcessTypeNameKey { get; }

    /// <summary>Emtia picker listesi (çalışılan şirket scope'lu).</summary>
    protected abstract Task<List<TListDto>> GetCommodityPickerListAsync(Guid? companyId);

    private const string LabelStyle = "font-weight:600; text-transform:uppercase; letter-spacing:0.05em;";

    private bool _isMobile;

    private VoucherLineDto _model = NewModel();
    private static VoucherLineDto NewModel() => new()
    {
        Direction   = ProcessDirectionType.Outbound,
        PaymentType = ProcessPaymentType.Normal,
        Factor      = 1m,
    };

    private List<TListDto> _allCommodities = new();
    private List<TListDto> _activeCommodities = new();

    private List<CurrencyUnitListDto> _activeUnits = new();
    private List<CashListDto> _allCashes = new();
    private Dictionary<Guid, string> _codeByUnit = new();

    private bool _showAdet, _priceByQuantity, _priceTypeReadOnly;

    private record DirectionItem(ProcessDirectionType Value, string Label);
    private List<DirectionItem> _directionItems = new();
    private record PaymentItem(ProcessPaymentType? Value, string Label);
    private List<PaymentItem> _paymentItems = new();
    private record PriceTypeItem(bool IsQuantity, string Label);
    private List<PriceTypeItem> _priceTypeItems = new();

    private record PayComboItem(Guid Id, string Code, bool IsActive, Guid? PayUnitId, string? PayUnitCode);
    private List<PayComboItem> _activePayItems = new();
    private PayComboItem? _selectedPayItem;

    protected override async Task OnInitializedAsync()
    {
        // Type türeyen sınıftan gelir — ilk render'dan önce (sync bölümde) atanır.
        _model.Type = ProcessType;

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
            new(false, L["Amount"].Value),
            new(true,  L["Count"].Value),
        };

        await Working.EnsureLoadedAsync();
        _allCommodities = await GetCommodityPickerListAsync(Working.CurrentCompanyId);
        _activeCommodities = _allCommodities.Where(c => c.IsActive).ToList();

        var cashResult = await CashService.GetListAsync(new CashListRequestDto { MaxResultCount = 1000 });
        _allCashes = cashResult.Items.ToList();

        var unitResult = await CurrencyUnitService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _activeUnits = unitResult.Items.Where(u => u.IsActive).ToList();
        _codeByUnit = unitResult.Items.ToDictionary(u => u.Id, u => u.Code);

        if (_activeCommodities.Count > 0)
            OnCommodityChanged(_activeCommodities[0].Id);
        else
            BuildPayList();
    }

    // Tutar = (Adet | Miktar) × Fiyat.
    private void Recompute()
    {
        var basis = _priceByQuantity ? _model.Quantity : _model.Amount;
        _model.PayTotal = basis * _model.PayFactor;
    }

    private void OnCommodityChanged(Guid? id)
    {
        var c = id.HasValue ? _allCommodities.FirstOrDefault(x => x.Id == id.Value) : null;
        _model.CommodityId   = id;
        _model.CommodityCode = c?.Code ?? string.Empty;
        if (c is { })
        {
            _showAdet          = c.IsQuantity;
            _priceByQuantity   = c.PriceByQuantity;
            _priceTypeReadOnly = !c.PriceTypeChange;
            ApplyDirectionPrice(c);
        }
        BuildPayList();
        EnsurePayItem(SuggestedUnit());
        Recompute();
    }

    private void ApplyDirectionPrice(TListDto c)
    {
        var inflow = _model.Direction.IsInflow();   // Giriş
        _model.PayFactor = inflow ? c.EntryPrice : c.ExitPrice;
    }

    private Guid? SuggestedUnit()
    {
        var c = _allCommodities.FirstOrDefault(x => x.Id == _model.CommodityId);
        if (c is null) return null;
        var inflow = _model.Direction.IsInflow();
        return inflow ? c.EntryPriceUnitId : c.ExitPriceUnitId;
    }

    private void BuildPayList()
    {
        if (_model.PaymentType == ProcessPaymentType.WithCash)
        {
            _activePayItems = _allCashes
                .Where(c => c.IsActive)
                .Select(c => new PayComboItem(c.Id, c.Code, true,
                                              c.FollowingUnitId == Guid.Empty ? null : c.FollowingUnitId,
                                              c.FollowingUnitCode))
                .ToList();
        }
        else
        {
            _activePayItems = _activeUnits
                .Select(u => new PayComboItem(u.Id, u.Code, true, u.Id, u.Code))
                .ToList();
        }
    }

    private void EnsurePayItem(Guid? preferUnit = null)
    {
        if (preferUnit is { } pu)
        {
            var match = _activePayItems.FirstOrDefault(x => x.PayUnitId == pu);
            if (match is { }) { ApplyPayItem(match); return; }
        }
        if (_model.PayCommodityId is null || _activePayItems.All(x => x.Id != _model.PayCommodityId))
            ApplyPayItem(_activePayItems.FirstOrDefault());
        else
            ApplyPayItem(_activePayItems.First(x => x.Id == _model.PayCommodityId));
    }

    private void ApplyPayItem(PayComboItem? item)
    {
        _selectedPayItem        = item;
        _model.PayCommodityId   = item?.Id;
        _model.PayCommodityCode = item?.Code;
        _model.PayUnitId        = item?.PayUnitId;
    }

    private void OnDirectionChanged(ProcessDirectionType value)
    {
        _model.Direction = value;
        var c = _allCommodities.FirstOrDefault(x => x.Id == _model.CommodityId);
        if (c is { }) ApplyDirectionPrice(c);
        EnsurePayItem(SuggestedUnit());
        Recompute();
    }

    private void OnPaymentTypeChanged(ProcessPaymentType? value)
    {
        _model.PaymentType = value;
        BuildPayList();
        EnsurePayItem(SuggestedUnit());
        Recompute();
    }

    private void OnPayItemChanged(Guid? id)
    {
        ApplyPayItem(id.HasValue ? _activePayItems.FirstOrDefault(x => x.Id == id.Value) : null);
        Recompute();
    }

    private void OnAmountChanged(decimal value)   { _model.Amount = value;    Recompute(); }
    private void OnQuantityChanged(decimal value) { _model.Quantity = value;  Recompute(); }
    private void OnPayFactorChanged(decimal value) { _model.PayFactor = value; Recompute(); }
    private void OnPriceTypeChanged(bool isQuantity) { _priceByQuantity = isQuantity; Recompute(); }

    // Tutar elle düzenlenince Fiyat'ı geri-hesapla.
    private void OnPayTotalChanged(decimal value)
    {
        _model.PayTotal = value;
        var basis = _priceByQuantity ? _model.Quantity : _model.Amount;
        if (basis != 0m) _model.PayFactor = value / basis;
    }

    private string GroupStyle() =>
        "display:flex; flex-direction:column; gap:4px; " + (_isMobile ? "width:100%;" : "width:120px; flex-shrink:0;");
    private string ControlStyle() => _isMobile ? "width:100%;" : "width:120px;";

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
        if (_model.CommodityId is null || _model.PayUnitId is null)
            return;

        Recompute();
        _model.VoucherId          = VoucherId;
        _model.CompanyId          = CompanyId;
        _model.BranchId           = BranchId;
        _model.VaultId            = VaultId;
        _model.AccountId          = AccountId;
        _model.SubAccountId       = SubAccountId;
        _model.VoucherDate        = VoucherDate;
        _model.VoucherDescription = VoucherDescription;
        _model.Type               = ProcessType;
        _model.MainUnitId         = Guid.Empty;
        _model.Factor             = 1m;
        _model.Total              = 0m;
        _model.Profit             = 0m;
        _model.PayUnitRate        = 0m;

        var wasEdit = _model.Id != Guid.Empty;
        VoucherLineDto result;
        try
        {
            result = await VoucherService.SaveLineAsync(_model);
        }
        catch (Exception ex)
        {
            Ui.ShowErrorToast(L["Voucher_LineSaveFailed", ex.Message].Value);
            return;
        }

        VoucherId        = result.VoucherId;
        _model.VoucherId = result.VoucherId;
        _model.Id        = Guid.Empty;
        await OnSaved.InvokeAsync(result);
        Ui.ShowSuccessToast(wasEdit ? L["Voucher_LineUpdated"].Value : L["Voucher_LineAdded"].Value);

        if (wasEdit) { await OnBack.InvokeAsync(); return; }

        _model.Amount   = 0m;
        _model.Quantity = 0m;
        _model.PayTotal = 0m;
        _model.Description = null;
    }

    public Task LoadForEditAsync(VoucherLineDto dto)
    {
        _model = dto;
        VoucherId = dto.VoucherId;
        var c = _allCommodities.FirstOrDefault(x => x.Id == dto.CommodityId);
        if (c is { })
        {
            _showAdet          = c.IsQuantity;
            _priceByQuantity   = c.PriceByQuantity;
            _priceTypeReadOnly = !c.PriceTypeChange;
        }
        BuildPayList();
        EnsurePayItem();
        StateHasChanged();
        return Task.CompletedTask;
    }
}
