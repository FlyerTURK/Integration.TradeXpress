using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Pozisyon raporu — kalıcı bakiye ledger'ını (poster çıktısı) birim bazında toplar, bilanço birimine
/// re-base eder, base-dışı net açığı (DURUM) verir. ANLIK (tüm geçmiş); scope = company/branch/vault.
/// </summary>
public interface IPositionReportAppService : IApplicationService
{
    Task<PositionReportResultDto> GetAsync(PositionReportFilterDto filter);
}
