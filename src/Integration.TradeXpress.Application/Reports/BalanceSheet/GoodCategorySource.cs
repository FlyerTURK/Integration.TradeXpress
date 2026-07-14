using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// GOOD (Mamül) maliyet-envanteri kategorisi — genel ticari malın fiziksel holding'i, mamülün TEMSİLİ maliyet fiyatı
/// (= ANA VARYANTININ <c>GoodVariantDetail.EntryPrice</c>; VP4 — fiyat varyanta taşındı) ile değerlenir. Fiyat-tipi
/// bayrağı (PriceByQuantity, basis) ise ana mamülde kalır. ProcessType.Good voucher on-hand × EntryPrice @ EntryPriceUnit.
/// </summary>
[ExposeServices(typeof(IBalanceSheetCategorySource))]
public class GoodCategorySource : CostInventoryCategorySourceBase, ITransientDependency
{
    private readonly IRepository<Good, Guid> _goods;
    private readonly IGoodPricingResolver _pricingResolver;
    private readonly IDataFilter _dataFilter;

    public GoodCategorySource(
        IRepository<Voucher, Guid> vouchers, IAsyncQueryableExecuter executer,
        IRepository<Good, Guid> goods, IGoodPricingResolver pricingResolver, IDataFilter dataFilter)
        : base(vouchers, executer)
    {
        _goods           = goods;
        _pricingResolver = pricingResolver;
        _dataFilter      = dataFilter;
    }

    public override int Order => 23;
    protected override ProcessType ProcessKind => ProcessType.Good;
    protected override string Category => BalanceSheetCategory.Good;

    protected override async Task<Dictionary<Guid, CommodityCost>> LoadCostsAsync(IReadOnlyCollection<Guid> commodityIds)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            // Basis bayrağı (PriceByQuantity) ana mamülde; fiyat/birim ANA VARYANT'tan (IGoodPricingResolver).
            var q = await _goods.GetQueryableAsync();
            var flags = await Executer.ToListAsync(
                q.Where(g => commodityIds.Contains(g.Id)).Select(g => new { g.Id, g.PriceByQuantity }));
            var pricing = await _pricingResolver.ResolveAsync(commodityIds);

            return flags.ToDictionary(
                r => r.Id,
                r => pricing.TryGetValue(r.Id, out var p)
                    ? new CommodityCost(p.EntryPrice, p.EntryPriceUnitId, r.PriceByQuantity)
                    : new CommodityCost(0m, null, r.PriceByQuantity));
        }
    }
}
