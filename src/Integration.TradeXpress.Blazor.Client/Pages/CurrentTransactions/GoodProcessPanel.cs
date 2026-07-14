using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Pages.Goods;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>Mamül fiş satırı paneli — markup + davranış <see cref="CommodityProcessPanelBase{TListDto}"/>'te.
/// Good ÇOK-VARYANTLI: emtia combo'sunun altında varyant combo'su + varyant-başı fiyat (GoodVariantDetail);
/// seçilen varyant fiş satırının fiyatını da belirler (VariantsHaveOwnPricing). Emtia lookup ✎/+ → GoodEditHost.</summary>
public class GoodProcessPanel : CommodityProcessPanelBase<GoodListDto>
{
    [Inject] private IGoodAppService GoodService { get; set; } = default!;

    protected override ProcessType ProcessType => ProcessType.Good;
    protected override string ProcessTypeNameKey => "Good";
    protected override bool SupportsVariants => true;
    protected override bool VariantsHaveOwnPricing => true;

    protected override Type? CommodityEditComponentType => typeof(GoodEditHost);
    protected override string? CommodityCreatePolicy => TradeXpressPermissions.Goods.Create;
    protected override string? CommodityUpdatePolicy => TradeXpressPermissions.Goods.Update;

    protected override Task<List<GoodListDto>> GetCommodityPickerListAsync(Guid? companyId)
        => GoodService.GetPickerListAsync(companyId);

    protected override Task<List<CommodityVariantOptionDto>> GetVariantOptionsAsync(Guid commodityId)
        => GoodService.GetVariantPickerListAsync(commodityId);
}
