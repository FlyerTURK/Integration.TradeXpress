using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Companies;

public interface ICompanyAppService : ICrudAppService<
    CompanyGetDto,
    CompanyListDto,
    Guid,
    CompanyListRequestDto,
    CompanyCreateDto,
    CompanyUpdateDto>
{
    /// <summary>Şirketi tüm şube + kasalarıyla (tam ağaç) okur — edit formundaki drill list'ler için.</summary>
    Task<CompanyTreeDto> GetTreeAsync(Guid id);

    /// <summary>
    /// Şirket + şube + kasa ağacını tek transaction'da kaydeder (in-memory commit). Id'siz çocuklar
    /// eklenir, girdide olmayan mevcut çocuklar silinir; değişmezler (en az 1 şube/kasa, tek HQ şube,
    /// tek varsayılan kasa) zorlanır.
    /// </summary>
    Task<CompanyTreeDto> SaveTreeAsync(CompanyTreeSaveDto input);
}
