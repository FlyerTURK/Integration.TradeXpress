using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Cari-hesap-BAĞIMSIZ işlem raporu — Company (ICurrentCompany'den zorlanır) + Branch/Vault (opsiyonel)
/// kapsamındaki TÜM voucher satırları, tarih aralıklı + tip filtreli, server-side sayfalı.
/// </summary>
public interface ITransactionReportAppService : IApplicationService
{
    /// <summary>Kapsam + tarih aralığındaki işlem satırları (VoucherDate→CreationTime sıralı, sayfalı).</summary>
    Task<PagedResultDto<TransactionReportRowDto>> GetListAsync(TransactionReportRequestDto request);
}
