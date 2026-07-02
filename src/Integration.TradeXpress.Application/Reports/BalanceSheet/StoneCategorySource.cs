using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// STONE (Taş) maliyet-envanteri kategorisi — kıymetli taşların fiziksel holding'i, <see cref="Stone.EntryPrice"/>
/// (giriş/maliyet fiyatı) ile değerlenir. ProcessType.Stone voucher on-hand × EntryPrice @ EntryPriceUnit (ERPPRO
/// <c>Stok.GetTasStoklari.Maliyet</c> paritesi). Host katalog (TenantId=null) dahil. Diğer mantık base'te.
/// </summary>
[ExposeServices(typeof(IBalanceSheetCategorySource))]
public class StoneCategorySource : CostInventoryCategorySourceBase, ITransientDependency
{
    private readonly IRepository<Stone, Guid> _stones;
    private readonly IDataFilter _dataFilter;

    public StoneCategorySource(
        IRepository<Voucher, Guid> vouchers, IAsyncQueryableExecuter executer,
        IRepository<Stone, Guid> stones, IDataFilter dataFilter)
        : base(vouchers, executer)
    {
        _stones     = stones;
        _dataFilter = dataFilter;
    }

    public override int Order => 20;
    protected override ProcessType ProcessKind => ProcessType.Stone;
    protected override string Category => BalanceSheetCategory.Stone;

    protected override async Task<Dictionary<Guid, CommodityCost>> LoadCostsAsync(IReadOnlyCollection<Guid> commodityIds)
    {
        // Host katalog (TenantId=null) + tenant kayıtları → tenant filtresi kapalı (taş tanımı host'ta olabilir).
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var q = await _stones.GetQueryableAsync();
            var rows = await Executer.ToListAsync(
                q.Where(s => commodityIds.Contains(s.Id))
                 .Select(s => new { s.Id, s.EntryPrice, s.EntryPriceUnitId, s.PriceByQuantity }));
            return rows.ToDictionary(r => r.Id, r => new CommodityCost(r.EntryPrice, r.EntryPriceUnitId, r.PriceByQuantity));
        }
    }
}
