using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.Framework.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// FiyatlÄ±-emtia fiÅŸ satÄ±rÄ± panellerinin (TaÅŸ/MÃ¼cevher) ortak gÃ¶vdesi â€” markup + TÃœM davranÄ±ÅŸ burada.
/// TÃ¼reyen sÄ±nÄ±f markup'sÄ±zdÄ±r (BuildRenderTree devralÄ±nÄ±r); yalnÄ±z iÅŸlem tipini, panel baÅŸlÄ±k anahtarÄ±nÄ±
/// ve emtia picker servisini saÄŸlar.
/// </summary>
public abstract partial class CommodityProcessPanelBase<TListDto> : IVoucherLineEditPanel where TListDto : class, IPricedCommodityListDto
{
    [Inject] protected IStringLocalizer<TradeXpressResource> L { get; set; } = default!;
    [Inject] private ICashAppService CashService { get; set; } = default!;
    [Inject] private ICurrencyUnitAppService CurrencyUnitService { get; set; } = default!;
    [Inject] private IVoucherAppService VoucherService { get; set; } = default!;
    [Inject] private IWorkingContextService Working { get; set; } = default!;
    [Inject] private IUiInteractionService Ui { get; set; } = default!;
    [Inject] private IViewOpener ViewOpener { get; set; } = default!;   // emtia/varyant lookup âœ/+ â†’ merkezÃ® popup yolu
    [Inject] private IPopupService PopupService { get; set; } = default!;

    /// <summary>KaydÄ±n normal fiÅŸ yoluna mÄ± Teyit yoluna mÄ± gideceÄŸinin TEK karar noktasÄ± (SSOT).</summary>
    [Inject] private VoucherLinePersister Persister { get; set; } = default!;

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

    /// <summary>Ä°Ã‡ KARÅI TARAF (Teyit) kipi: doluysa satÄ±r POSTLANMAZ â€” Teyit teklifi kurulur. Null = bugÃ¼nkÃ¼
    /// normal cari akÄ±ÅŸÄ± (davranÄ±ÅŸ birebir aynÄ±). <i>(Bu panel hiyerarÅŸisi henÃ¼z <see cref="VoucherLineContext"/>'e
    /// geÃ§medi â€” spec'teki olgunlaÅŸtÄ±rma iÅŸi; ÅŸimdilik eklemeli tek parametre.)</i></summary>
    [Parameter] public Guid? CounterpartyVaultId { get; set; }

    /// <summary>BEYAN kipi (gelen kutusundan "Kendi GiriÅŸimi Yaz"): doluysa yeni teklif aÃ§Ä±lmaz, bu Teyit'e
    /// alÄ±cÄ±nÄ±n KENDÄ° satÄ±rÄ± yazÄ±lÄ±r (sunucu ayna doÄŸrular).</summary>
    [Parameter] public Guid? DeclareConfirmationId { get; set; }

    /// <summary>Teyit yoluna gidildiÄŸinde (teklif/beyan) tetiklenir â€” fiÅŸ OLUÅMADIÄI iÃ§in <see cref="OnSaved"/>
    /// tetiklenmez. Gelen kutusu bunu dinleyip popup'Ä± kapatÄ±r/listeyi tazeler.</summary>
    [Parameter] public EventCallback<VoucherLinePersistOutcome> OnConfirmationSubmitted { get; set; }

    // â”€â”€ TÃ¼reyen sÄ±nÄ±fÄ±n saÄŸladÄ±klarÄ± â”€â”€

    /// <summary>SatÄ±rÄ±n iÅŸlem tipi (Stone/Jewelry).</summary>
    protected abstract ProcessType ProcessType { get; }

    /// <summary>Panel baÅŸlÄ±ÄŸÄ±nÄ±n lokalizasyon anahtarÄ± ("Stone"/"Jewelry").</summary>
    protected abstract string ProcessTypeNameKey { get; }

    /// <summary>Emtia picker listesi (Ã§alÄ±ÅŸÄ±lan ÅŸirket scope'lu).</summary>
    protected abstract Task<List<TListDto>> GetCommodityPickerListAsync(Guid? companyId);

    /// <summary>Panel varyant destekliyor mu (yalnÄ±z Good gibi Ã§ok-varyantlÄ± emtiada true) â€” base varyant combo'sunu buna gÃ¶re Ã§izer.</summary>
    protected virtual bool SupportsVariants
    {
        get { return false; }
    }

    /// <summary>VaryantlarÄ±n KENDÄ° fiyatÄ± var mÄ± (Good â†’ GoodVariantDetail). false ise varyant seÃ§imi fiyatÄ± DEÄÄ°ÅTÄ°RMEZ
    /// (yalnÄ±z VariantId kaydeder); fiyat emtia seviyesinde kalÄ±r (Jewelry/Stone: milyem/manuel fiyat).</summary>
    protected virtual bool VariantsHaveOwnPricing
    {
        get { return false; }
    }

    /// <summary>SeÃ§ili emtianÄ±n (commodityId) AKTÄ°F varyant seÃ§eneklerini yÃ¼kler â€” yalnÄ±z SupportsVariants panelde override edilir.</summary>
    protected virtual Task<List<CommodityVariantOptionDto>> GetVariantOptionsAsync(Guid commodityId)
    {
        return Task.FromResult(new List<CommodityVariantOptionDto>());
    }

    /// <summary>Emtia lookup combo'sunun âœ/+ butonlarÄ±nÄ±n aÃ§tÄ±ÄŸÄ± edit host tipi (Ã¶r. <c>typeof(GoodEditHost)</c>). null â†’ butonlar gizli.</summary>
    protected virtual Type? CommodityEditComponentType
    {
        get { return null; }
    }

    /// <summary>Emtia Ekle(+) izin adÄ± (UI kapÄ±sÄ±; server-side policy asÄ±l denetim). null â†’ kÄ±sÄ±t yok.</summary>
    protected virtual string? CommodityCreatePolicy
    {
        get { return null; }
    }

    /// <summary>Emtia DÃ¼zelt(âœ) izin adÄ±. null â†’ kÄ±sÄ±t yok.</summary>
    protected virtual string? CommodityUpdatePolicy
    {
        get { return null; }
    }

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
    private List<CommodityVariantOptionDto> _variantOptions = new();

    // YÃ¶n-bazlÄ± fiyat/birim kaynaÄŸÄ± â€” emtia ya da (varsa) seÃ§ili varyanttan doldurulur.
    private decimal _srcEntryPrice, _srcExitPrice;
    private Guid? _srcEntryUnitId, _srcExitUnitId;

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
        // Type tÃ¼reyen sÄ±nÄ±ftan gelir â€” ilk render'dan Ã¶nce (sync bÃ¶lÃ¼mde) atanÄ±r.
        _model.Type = ProcessType;

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
        {
            await OnCommodityChangedAsync(_activeCommodities[0].Id);
        }
        else
        {
            BuildPayList();
        }
    }

    // Tutar = (Adet | Miktar) Ã— Fiyat.
    private void Recompute()
    {
        var basis = _priceByQuantity ? _model.Quantity : _model.Amount;
        _model.PayTotal = basis * _model.PayFactor;
    }

    private async Task OnCommodityChangedAsync(Guid? id)
    {
        var c = id.HasValue ? _allCommodities.FirstOrDefault(x => x.Id == id.Value) : null;
        _model.CommodityId   = id;
        _model.CommodityCode = c?.Code ?? string.Empty;
        _model.VariantId     = null;
        _model.VariantCode   = null;
        _variantOptions      = new();
        if (c is { })
        {
            _showAdet          = c.IsQuantity;
            _priceByQuantity   = c.PriceByQuantity;
            _priceTypeReadOnly = !c.PriceTypeChange;
            SetPriceSource(c.EntryPrice, c.ExitPrice, c.EntryPriceUnitId, c.ExitPriceUnitId);

            // VaryantlÄ± emtia (Good): AKTÄ°F varyantlarÄ± yÃ¼kle. Tek varyant â†’ VariantId null (anlamlÄ± varyant boyutu yok);
            // Ã§oklu â†’ ana varyant varsayÄ±lan seÃ§ilir, fiyatÄ± (varsa) o belirler.
            if (SupportsVariants && id is { } commodityId)
            {
                _variantOptions = await GetVariantOptionsAsync(commodityId);
                if (_variantOptions.Count > 1)
                {
                    var main = _variantOptions.FirstOrDefault(v => v.IsMain) ?? _variantOptions[0];
                    ApplyVariant(main);
                }
            }
        }

        ApplyDirectionPrice();
        BuildPayList();
        EnsurePayItem(SuggestedUnit());
        Recompute();
    }

    private void OnVariantChanged(Guid? id)
    {
        var v = id.HasValue ? _variantOptions.FirstOrDefault(x => x.Id == id.Value) : null;
        if (v is { })
        {
            ApplyVariant(v);
        }
        else
        {
            _model.VariantId   = null;
            _model.VariantCode = null;
        }

        ApplyDirectionPrice();
        EnsurePayItem(SuggestedUnit());
        Recompute();
    }

    // SeÃ§ili varyantÄ± modele iÅŸler â€” VariantId/Code + (varyant-baÅŸÄ± fiyatÄ± olan emtiada) fiyat kaynaÄŸÄ±nÄ± varyanta Ã§evirir.
    private void ApplyVariant(CommodityVariantOptionDto v)
    {
        _model.VariantId   = v.Id;
        _model.VariantCode = v.Code;
        if (VariantsHaveOwnPricing)
        {
            SetPriceSource(v.EntryPrice, v.ExitPrice, v.EntryPriceUnitId, v.ExitPriceUnitId);
        }
    }

    // Emtia veya seÃ§ili varyanttan yÃ¶n-bazlÄ± fiyat/birim kaynaÄŸÄ±nÄ± belirler (ApplyDirectionPrice/SuggestedUnit buradan okur).
    private void SetPriceSource(decimal entry, decimal exit, Guid? entryUnitId, Guid? exitUnitId)
    {
        _srcEntryPrice  = entry;
        _srcExitPrice   = exit;
        _srcEntryUnitId = entryUnitId;
        _srcExitUnitId  = exitUnitId;
    }

    private void ApplyDirectionPrice()
    {
        var inflow = _model.Direction.IsInflow();   // GiriÅŸ
        _model.PayFactor = inflow ? _srcEntryPrice : _srcExitPrice;
    }

    private Guid? SuggestedUnit()
    {
        if (_model.CommodityId is null)
        {
            return null;
        }

        var inflow = _model.Direction.IsInflow();
        return inflow ? _srcEntryUnitId : _srcExitUnitId;
    }

    // Emtia lookup âœ/+ sonrasÄ± (EntityChange) listeyi tazele + seÃ§ili emtia varyantlarÄ±nÄ± da yenile.
    private async Task ReloadCommoditiesAsync()
    {
        _allCommodities = await GetCommodityPickerListAsync(Working.CurrentCompanyId);
        _activeCommodities = _allCommodities.Where(c => c.IsActive).ToList();
        if (SupportsVariants && _model.CommodityId is { } id)
        {
            _variantOptions = await GetVariantOptionsAsync(id);
        }

        StateHasChanged();
    }

    // Varyant lookup'Ä±n tazeleme kancasÄ± (TItem farklÄ± olduÄŸundan EntityChange doÄŸrudan tetiklemez; el ile).
    private async Task ReloadVariantsAsync()
    {
        if (SupportsVariants && _model.CommodityId is { } id)
        {
            _variantOptions = await GetVariantOptionsAsync(id);
            StateHasChanged();
        }
    }

    // SeÃ§ili emtianÄ±n KARTINI aÃ§ar (varyant yÃ¶netimi orada) â€” commodity id ile (varyant id DEÄÄ°L). Varyant lookup'Ä±n âœ/+ butonlarÄ± buraya baÄŸlanÄ±r.
    private Task OpenCommodityCardAsync()
    {
        if (CommodityEditComponentType is null || _model.CommodityId is not { } id)
        {
            return Task.CompletedTask;
        }

        var extra = new Dictionary<string, object>
        {
            { "OnClosed", EventCallback.Factory.Create(this, () => PopupService.Close()) },
        };
        return ViewOpener.OpenAsync(CommodityEditComponentType, id, string.Empty, null, extra);
    }

    // Varyant Ekle(+) â†’ seÃ§ili mamÃ¼lÃ¼n kartÄ±nÄ± aÃ§ar (yeni varyant orada nitelik tanÄ±mÄ±yla Ã¼retilir); combo'ya seÃ§ilecek deÄŸer yok.
    private async Task<Guid?> OpenCommodityCardForAddAsync()
    {
        await OpenCommodityCardAsync();
        return null;
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
        if (_model.CommodityId is not null)
        {
            ApplyDirectionPrice();
        }

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

    // Tutar elle dÃ¼zenlenince Fiyat'Ä± geri-hesapla.
    private void OnPayTotalChanged(decimal value)
    {
        _model.PayTotal = value;
        var basis = _priceByQuantity ? _model.Quantity : _model.Amount;
        if (basis != 0m) _model.PayFactor = value / basis;
    }

    private string GroupStyle() =>
        "display:flex; flex-direction:column; gap:4px; " + (_isMobile ? "width:100%;" : "width:120px; flex-shrink:0;");
    private string ControlStyle() => _isMobile ? "width:100%;" : "width:120px;";

    // Varyant grubu â€” emtia+varyant combo'larÄ± alt alta; emtia combo'sunun 2 KATI geniÅŸlik (Good paneli).
    private string VariantGroupStyle() =>
        "display:flex; flex-direction:column; gap:4px; " + (_isMobile ? "width:100%;" : "width:240px; flex-shrink:0;");
    private string VariantControlStyle() => _isMobile ? "width:100%;" : "width:240px;";

    /// <summary>Kaydetme sÃ¼rÃ¼yor mu â€” re-entrancy bayraÄŸÄ± (Ã§ift tÄ±klama/Enter Ã§ift-gÃ¶nderim korumasÄ±).</summary>
    private bool _saving;

    private async Task HandleSave()
    {
        if (_saving) return; // kaydetme zaten sÃ¼rÃ¼yor â€” Ã§ift tÄ±klamayÄ± yut
        _saving = true;
        StateHasChanged(); // Kaydet butonu ilk await'te disabled Ã§izilsin
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

        // KararÄ± persister verir (TEK yer): dÄ±ÅŸ cari â†’ normal fiÅŸ kaydÄ± Â· iÃ§ kasa â†’ Teyit teklifi Â·
        // beyan kipi â†’ alÄ±cÄ±nÄ±n kendi satÄ±rÄ±. Teyit yollarÄ±nda fiÅŸ OLUÅMAZ â†’ result.Line null.
        VoucherLinePersistResult persisted;
        try
        {
            persisted = await Persister.PersistAsync(new VoucherLinePersistRequest(
                _model, CounterpartyVaultId, VaultId, DeclareConfirmationId));
        }
        catch (Exception ex)
        {
            Ui.ShowErrorToast(L["Voucher_LineSaveFailed", ex.Message].Value);
            return;
        }

        if (persisted.Line is not { } result)
        {
            // Teyit kuruldu/beyan edildi ya da Ã¶n koÅŸul saÄŸlanmadÄ±: fiÅŸ/grid durumu ELLENMEZ, toast'Ä± persister verdi.
            ResetVolatileFields();
            if (persisted.Outcome != VoucherLinePersistOutcome.Blocked)
            {
                await OnConfirmationSubmitted.InvokeAsync(persisted.Outcome);
            }
            return;
        }

        VoucherId        = result.VoucherId;
        _model.VoucherId = result.VoucherId;
        _model.Id        = Guid.Empty;
        await OnSaved.InvokeAsync(result);
        Ui.ShowSuccessToast(wasEdit ? L["Voucher_LineUpdated"].Value : L["Voucher_LineAdded"].Value);

        if (wasEdit) { await OnBack.InvokeAsync(); return; }

        ResetVolatileFields();
    }

    /// <summary>Yeni ekleme sonrasÄ± bir sonraki satÄ±r iÃ§in uÃ§ucu alanlarÄ± sÄ±fÄ±rlar (tutarlar/aÃ§Ä±klama;
    /// sÄ±nÄ±flandÄ±rma ve seÃ§imler kalÄ±r).</summary>
    private void ResetVolatileFields()
    {
        _model.Amount      = 0m;
        _model.Quantity    = 0m;
        _model.PayTotal    = 0m;
        _model.Description = null;
    }

    public async Task LoadForEditAsync(VoucherLineDto dto)
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

        // VaryantlÄ± emtiada varyant combo'sunu kayÄ±tlÄ± VariantId ile gÃ¶sterebilmek iÃ§in seÃ§enekleri yÃ¼kle (fiyat WYSIWYG â€” dokunma).
        if (SupportsVariants && dto.CommodityId is { } commodityId)
        {
            _variantOptions = await GetVariantOptionsAsync(commodityId);
        }

        BuildPayList();
        EnsurePayItem();
        StateHasChanged();
    }
}

