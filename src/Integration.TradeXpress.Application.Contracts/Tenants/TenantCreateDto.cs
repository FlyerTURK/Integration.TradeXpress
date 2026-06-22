using System.Collections.Generic;
using Integration.Framework.Base.Dtos.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Integration.TradeXpress.Tenants;

public class TenantCreateDto : ICreateDto
{
    [Required]
    [StringLength(64)]
    public string Name { get; set; } = string.Empty;

    // Onboarding'de bellekte toplanan kullanıcılar (DrillList). En az bir satır IsAdmin olmalı → tenant
    // yöneticisi. Diğerleri ek kullanıcı. (Tek admin alanları kaldırıldı; admin bu listeden gelir.)
    public List<TenantUserInput> Users { get; set; } = new();

    // Onboarding'de bellekte toplanan şirketler (DrillList). Bir satır IsHeadquarters olmalı (HQ).
    public List<TenantCompanyInput> Companies { get; set; } = new();
}
