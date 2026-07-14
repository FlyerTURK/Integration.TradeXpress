using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Pages.Stones;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>Taş fiş satırı paneli — markup + davranış <see cref="CommodityProcessPanelBase{TListDto}"/>'te.
/// Varyant destekli ama varyant-başı fiyat YOK (fiyat taş seviyesinde). Emtia lookup ✎/+ → StoneEditHost.</summary>
public class StoneProcessPanel : CommodityProcessPanelBase<StoneListDto>
{
    [Inject] private IStoneAppService StoneService { get; set; } = default!;

    protected override ProcessType ProcessType => ProcessType.Stone;
    protected override string ProcessTypeNameKey => "Stone";
    protected override bool SupportsVariants => true;

    protected override Type? CommodityEditComponentType => typeof(StoneEditHost);
    protected override string? CommodityCreatePolicy => TradeXpressPermissions.Stones.Create;
    protected override string? CommodityUpdatePolicy => TradeXpressPermissions.Stones.Update;

    protected override Task<List<StoneListDto>> GetCommodityPickerListAsync(Guid? companyId)
        => StoneService.GetPickerListAsync(companyId);

    protected override Task<List<CommodityVariantOptionDto>> GetVariantOptionsAsync(Guid commodityId)
        => StoneService.GetVariantPickerListAsync(commodityId);
}
