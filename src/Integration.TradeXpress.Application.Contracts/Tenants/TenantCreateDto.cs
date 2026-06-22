using System.Collections.Generic;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Companies;
using System.ComponentModel.DataAnnotations;

namespace Integration.TradeXpress.Tenants;

public class TenantCreateDto : ICreateDto
{
    [Required]
    [StringLength(64)]
    public string Name { get; set; } = string.Empty;

    // Onboarding'de bellekte toplanan kullanıcılar (DrillList). En az bir satır IsAdmin olmalı → tenant
    // yöneticisi. Diğerleri ek kullanıcı.
    public List<TenantUserInput> Users { get; set; } = new();

    // Onboarding'de bellekte toplanan şirketler (tam graf: şirket→şube→kasa). Bir satır IsHeadquarters
    // olmalı (HQ). Kayıtta her şirket CompanyAppService'e delege edilir.
    public List<CompanyGraphDto> Companies { get; set; } = new();
}
