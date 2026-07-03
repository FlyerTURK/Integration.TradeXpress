using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>
/// Borç/Alacak Dekontu paneli (legacy BORC=999): tek bacak, Miktar YOK (0 gider), peşin muafiyeti YOK
/// (DebitNoteBalancePoster daima bakiyeye yazar). Yön etiketi ALACAK/BORÇ — DB'de Inbound/Outbound saklanır
/// (legacy quirk paritesi). Kategori bu fazda serbest metin → CommodityCode (legacy 'DEVİR' gibi).
/// </summary>
public partial class DebitNoteProcessPanel
{
    [Parameter] public EventCallback OnBack { get; set; }
    [Parameter] public string? AccountCode { get; set; }
    [Parameter] public string? SubAccountCode { get; set; }

    [Parameter] public Guid CompanyId { get; set; }
    [Parameter] public Guid BranchId { get; set; }
    [Parameter] public Guid? VaultId { get; set; }
    [Parameter] public Guid AccountId { get; set; }
    [Parameter] public Guid? SubAccountId { get; set; }
    [Parameter] public DateTime VoucherDate { get; set; } = DateTime.Now;
    [Parameter] public string? VoucherDescription { get; set; }
    [Parameter] public Guid? VoucherId { get; set; }
    [Parameter] public EventCallback<VoucherLineDto> OnSaved { get; set; }

    private bool _isMobile;

    private VoucherLineDto _model = NewModel();

    private List<CurrencyUnitListDto> _activeUnits = new();

    private sealed record DirectionItem(ProcessDirectionType Value, string Label);
    private List<DirectionItem> _directionItems = new();

    private static VoucherLineDto NewModel()
    {
        return new VoucherLineDto
        {
            Type      = ProcessType.DebitNote,
            Direction = ProcessDirectionType.Inbound,   // ALACAK varsayılan (etiket; DB'de Giriş)
        };
    }

    protected override async Task OnInitializedAsync()
    {
        // Yön combo — legacy quirk: görsel etiket ALACAK/BORÇ, DB'de GİRİŞ/ÇIKIŞ saklanır.
        _directionItems = new()
        {
            new(ProcessDirectionType.Inbound,  L["Enum:ProcessDirectionType:Credit"].Value),
            new(ProcessDirectionType.Outbound, L["Enum:ProcessDirectionType:Debit"].Value),
        };

        var unitResult = await CurrencyUnitService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 1000 });
        _activeUnits = unitResult.Items.Where(u => u.IsActive).ToList();

        if (_model.PayUnitId is null && _activeUnits.Count > 0)
        {
            OnUnitChanged(_activeUnits[0].Id);
        }
    }

    private void OnCategoryChanged(string value)
    {
        // Legacy kategori kodları büyük harf ('DEVİR' gibi) — Türkçe-duyarlı upper.
        _model.CommodityCode = (value ?? string.Empty).Trim().ToUpper(CultureInfo.CurrentCulture);
    }

    private void OnAmountChanged(decimal value)
    {
        // Tutar = karşılık bacağı (PayTotal); CashBalancePoster ailesiyle aynı alan.
        _model.PayFactor = value;
        _model.PayTotal  = value;
    }

    private void OnUnitChanged(Guid? id)
    {
        var unit = id.HasValue ? _activeUnits.FirstOrDefault(u => u.Id == id.Value) : null;
        _model.PayUnitId        = id;
        _model.PayCommodityId   = id;
        _model.PayCommodityCode = unit?.Code;
    }

    private string GroupStyle()
    {
        return "display:flex; flex-direction:column; gap:4px; " + (_isMobile ? "width:100%;" : "width:120px; flex-shrink:0;");
    }

    private string ControlStyle()
    {
        return _isMobile ? "width:100%;" : "width:120px;";
    }

    private async Task HandleSave()
    {
        if (_model.PayUnitId is null || _model.PayTotal == 0m)
        {
            return; // birim seçili değil ya da tutar girilmemiş
        }

        _model.VoucherId          = VoucherId;
        _model.CompanyId          = CompanyId;
        _model.BranchId           = BranchId;
        _model.VaultId            = VaultId;
        _model.AccountId          = AccountId;
        _model.SubAccountId       = SubAccountId;
        _model.VoucherDate        = VoucherDate;
        _model.VoucherDescription = VoucherDescription;
        _model.Type               = ProcessType.DebitNote;
        _model.PaymentType        = null;                 // dekontta ödeme tipi yok
        // Ana bacak boş: Miktar alanı YOK (legacy 0 gider), parasal etki pay-leg'de.
        _model.MainUnitId = Guid.Empty;
        _model.Quantity   = 0m;
        _model.Amount     = 0m;
        _model.Factor     = 0m;
        _model.Total      = 0m;
        _model.Profit     = 0m;

        var wasEdit = _model.Id != Guid.Empty;   // save Id'yi dolduracağı için ÖNCE yakala

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

        if (wasEdit)
        {
            await OnBack.InvokeAsync();
            return;
        }

        _model.PayFactor   = 0m;
        _model.PayTotal    = 0m;
        _model.Description = null;
    }

    /// <summary>Düzeltme: GetDto'yu model olarak alır (combo/birim seçimleri dto alanlarına bağlı).</summary>
    public Task LoadForEditAsync(VoucherLineDto dto)
    {
        _model    = dto;
        VoucherId = dto.VoucherId;
        StateHasChanged();
        return Task.CompletedTask;
    }
}
