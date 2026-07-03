using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>Mücevher fiş satırı paneli — markup + davranış <see cref="CommodityProcessPanelBase{TListDto}"/>'te.</summary>
public class JewelryProcessPanel : CommodityProcessPanelBase<JewelryListDto>
{
    [Inject] private IJewelryAppService JewelryService { get; set; } = default!;

    protected override ProcessType ProcessType => ProcessType.Jewelry;
    protected override string ProcessTypeNameKey => "Jewelry";

    protected override Task<List<JewelryListDto>> GetCommodityPickerListAsync(Guid? companyId)
        => JewelryService.GetPickerListAsync(companyId);
}
