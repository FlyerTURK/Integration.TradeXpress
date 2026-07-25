using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    /// <summary>Geçerli tenant'ın TÜM şirketlerini şube+kasa grafıyla döner — tenant edit formunun Şirketler drill
    /// grid'i bunu tüketir. Tek şirketlik <c>GetAsync</c> ile AYNI okuyucuyu kullanır (kopya graf kodu YOK).
    /// <para>Çağıran tenant kapsamını kendisi kurar: <c>AppCompanies</c> IMultiTenant'tır, host bağlamında
    /// (CurrentTenant=null) sorgu tenant satırlarını GÖRMEZ.</para></summary>
    Task<List<CompanyGraphDto>> GetGraphListAsync();
}
