using System;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Companies;

// Şirket + şube + kasa grafı standart Create/Update/Get üzerinden taşınır (CompanyGetDto.Branches).
// Ayrı tree-API yok; yazımlar BranchAppService'e (o da VaultAppService'e) delege edilir.
public interface ICompanyAppService : ICrudAppService<
    CompanyGetDto,
    CompanyListDto,
    Guid,
    CompanyListRequestDto,
    CompanyCreateDto,
    CompanyUpdateDto>
{
}
