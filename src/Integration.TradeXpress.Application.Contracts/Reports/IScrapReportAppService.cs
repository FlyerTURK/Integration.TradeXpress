using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Reports;

public interface IScrapReportAppService : IApplicationService
{
    Task<List<ScrapStockRowDto>> GetStockAsync(ScrapReportFilterDto filter);
    Task<List<ScrapMovementRowDto>> GetMovementsAsync(ScrapReportFilterDto filter);
}
