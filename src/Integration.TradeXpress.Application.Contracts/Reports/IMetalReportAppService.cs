using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Reports;

public interface IMetalReportAppService : IApplicationService
{
    Task<List<MetalStockRowDto>> GetStockAsync(MetalReportFilterDto filter);
    Task<List<MetalMovementRowDto>> GetMovementsAsync(MetalReportFilterDto filter);
}
