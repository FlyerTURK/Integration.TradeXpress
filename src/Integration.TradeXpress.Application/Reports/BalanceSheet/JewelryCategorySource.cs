using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// JEWELRY (Mücevher) maliyet-envanteri kategorisi — kıymetli taş barındıran takıların fiziksel holding'i,
/// <see cref="Jewelry.EntryPrice"/> (giriş/maliyet fiyatı) ile değerlenir. ProcessType.Jewelry voucher on-hand ×
/// EntryPrice @ EntryPriceUnit. ERPPRO bunu yanlışlıkla "PIRLANTA" adlandırmıştı (= mücevher). Host katalog dahil.
/// </summary>
[ExposeServices(typeof(IBalanceSheetCategorySource))]
public class JewelryCategorySource : CostInventoryCategorySourceBase, ITransientDependency
{
    private readonly IRepository<Jewelry, Guid> _jewelries;
    private readonly IDataFilter _dataFilter;

    public JewelryCategorySource(
        IRepository<Voucher, Guid> vouchers, IAsyncQueryableExecuter executer,
        IRepository<Jewelry, Guid> jewelries, IDataFilter dataFilter)
        : base(vouchers, executer)
    {
        _jewelries  = jewelries;
        _dataFilter = dataFilter;
    }

    public override int Order => 21;
    protected override ProcessType ProcessKind => ProcessType.Jewelry;
    protected override string Category => BalanceSheetCategory.Jewelry;

    protected override async Task<Dictionary<Guid, CommodityCost>> LoadCostsAsync(IReadOnlyCollection<Guid> commodityIds)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var q = await _jewelries.GetQueryableAsync();
            var rows = await Executer.ToListAsync(
                q.Where(j => commodityIds.Contains(j.Id))
                 .Select(j => new { j.Id, j.EntryPrice, j.EntryPriceUnitId, j.PriceByQuantity }));
            return rows.ToDictionary(r => r.Id, r => new CommodityCost(r.EntryPrice, r.EntryPriceUnitId, r.PriceByQuantity));
        }
    }
}
