using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil hesaplama beslemesi (M3) — saf <c>SubstitutionSolver</c>'ı GERÇEK verilerle (grup tanımı +
/// kullanılabilir stok + satış-kuru maliyeti) besleyip kullanıcı tablosu formatında sonuç döner.
/// Tolerans DAİMA grup ayarından gelir (override yok — konsept kararı); stok kapsamı
/// <c>MetalReportAppService.GetStockAsync</c> ile birebir aynı (working company + opsiyonel şube/kasa).
/// </summary>
public interface ISubstitutionCalculationAppService : IApplicationService
{
    Task<SubstitutionCalculationResultDto> CalculateAsync(SubstitutionCalculationInput input);
}
