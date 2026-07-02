using System;
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

    /// <summary>Bilanço STOK(nakit) için: kapsam (şirket ICurrentCompany'den) + branch/vault, asOfExclusive'den ÖNCE
    /// birikmiş net nakit holding'i birim-bazında (firma-perspektifi +). Gün-sonu dahil için asOfExclusive=asOf.Date.AddDays(1).</summary>
    Task<Dictionary<Guid, decimal>> GetCashNetByUnitAsync(Guid? branchId, Guid? vaultId, DateTime asOfExclusive);
}
