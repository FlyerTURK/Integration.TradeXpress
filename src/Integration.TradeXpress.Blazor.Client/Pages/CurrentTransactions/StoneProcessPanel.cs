using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>Taş fiş satırı paneli — markup + davranış <see cref="CommodityProcessPanelBase{TListDto}"/>'te.</summary>
public class StoneProcessPanel : CommodityProcessPanelBase<StoneListDto>
{
    [Inject] private IStoneAppService StoneService { get; set; } = default!;

    protected override ProcessType ProcessType => ProcessType.Stone;
    protected override string ProcessTypeNameKey => "Stone";

    protected override Task<List<StoneListDto>> GetCommodityPickerListAsync(Guid? companyId)
        => StoneService.GetPickerListAsync(companyId);
}
