using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Pages.Jewelries;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>Mücevher fiş satırı paneli — markup + davranış <see cref="CommodityProcessPanelBase{TListDto}"/>'te.
/// Varyant destekli (agnostik varyant sistemi) ama varyant-başı fiyat YOK (VariantsHaveOwnPricing=false →
/// varyant seçimi yalnız VariantId kaydeder; fiyat mücevher seviyesinde kalır). Emtia lookup ✎/+ → JewelryEditHost.</summary>
public class JewelryProcessPanel : CommodityProcessPanelBase<JewelryListDto>
{
    [Inject] private IJewelryAppService JewelryService { get; set; } = default!;

    protected override ProcessType ProcessType => ProcessType.Jewelry;
    protected override string ProcessTypeNameKey => "Jewelry";
    protected override bool SupportsVariants => true;

    protected override Type? CommodityEditComponentType => typeof(JewelryEditHost);
    protected override string? CommodityCreatePolicy => TradeXpressPermissions.Jewelries.Create;
    protected override string? CommodityUpdatePolicy => TradeXpressPermissions.Jewelries.Update;

    protected override Task<List<JewelryListDto>> GetCommodityPickerListAsync(Guid? companyId)
        => JewelryService.GetPickerListAsync(companyId);

    protected override Task<List<CommodityVariantOptionDto>> GetVariantOptionsAsync(Guid commodityId)
        => JewelryService.GetVariantPickerListAsync(commodityId);
}
