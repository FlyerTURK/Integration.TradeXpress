using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Reports;

public interface IGoodReportAppService : IApplicationService
{
    Task<List<GoodStockRowDto>> GetStockAsync(GoodReportFilterDto filter);
    Task<List<GoodMovementRowDto>> GetMovementsAsync(GoodReportFilterDto filter);
}
