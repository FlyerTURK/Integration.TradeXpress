using System;
using System.Collections.Generic;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Companies;

namespace Integration.TradeXpress.Tenants;

public class TenantGetDto : Volo.Abp.Application.Dtos.EntityDto<Guid>, IGetDto<Guid>
{
    public string Name { get; set; } = string.Empty;

    // Yalnızca Create onboarding'inde UI binding için (in-memory DrillList'ler). Mevcut tenant düzenlemede boş.
    // Şirketler tam graf (şirket→şube→kasa); kayıtta CompanyAppService'e delege edilir.
    public List<TenantUserInput> Users { get; set; } = new();
    public List<CompanyGraphDto> Companies { get; set; } = new();

    // Akıllı Zıplama için
    public int PageIndex { get; set; }
}
