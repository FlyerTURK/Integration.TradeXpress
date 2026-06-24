using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Reports;

public interface ICashReportAppService : IApplicationService
{
    /// <summary>Kapsam içindeki nakit stoğu (para birimi bazında net). Anlık (tarih yok sayılır).</summary>
    Task<List<CashStockRowDto>> GetStockAsync(CashReportFilterDto filter);

    /// <summary>Kapsam + tarih aralığındaki nakit hareketleri (satır listesi).</summary>
    Task<List<CashMovementRowDto>> GetMovementsAsync(CashReportFilterDto filter);
}
