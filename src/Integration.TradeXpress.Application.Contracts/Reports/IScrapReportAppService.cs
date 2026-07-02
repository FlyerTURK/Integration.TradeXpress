using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Reports;

public interface IScrapReportAppService : IApplicationService
{
    Task<List<ScrapStockRowDto>> GetStockAsync(ScrapReportFilterDto filter);
    Task<List<ScrapMovementRowDto>> GetMovementsAsync(ScrapReportFilterDto filter);

    /// <summary>Bilanço STOK(hurda) için: kapsam (şirket ICurrentCompany'den) + branch/vault, asOfExclusive'den ÖNCE
    /// birikmiş fiziksel hurda-maden holding'i MainUnit(HAS)-bazında (Total=HAS, tüm ödeme tipleri; firma-perspektifi +).
    /// Gün-sonu dahil için asOfExclusive=asOf.Date.AddDays(1).</summary>
    Task<Dictionary<Guid, decimal>> GetScrapNetByUnitAsync(Guid? branchId, Guid? vaultId, DateTime asOfExclusive);

    /// <summary>DRILL — hurda stok COMMODITY bazında, tek birim için (bilanço Stok popup).</summary>
    Task<Dictionary<string, decimal>> GetScrapStockByCommodityAsync(Guid? branchId, Guid unitId, DateTime asOfExclusive);
}
