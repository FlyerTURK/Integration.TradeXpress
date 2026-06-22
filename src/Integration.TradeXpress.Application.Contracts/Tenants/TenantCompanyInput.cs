using System;
using System.ComponentModel.DataAnnotations;

namespace Integration.TradeXpress.Tenants;

/// <summary>
/// Yeni tenant onboarding'inde bellekte tutulan şirket satırı (Company DrillList öğesi). Tenant
/// kaydedilince her satır o tenant'ın scope'unda gerçek şirkete dönüşür (biri HQ olmalı).
/// </summary>
public class TenantCompanyInput
{
    /// <summary>DrillList satır anahtarı (sunucuya gitmez; yalnız grid kimliği).</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(Companies.CompanyConsts.CodeMaxLength, MinimumLength = 2)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(Companies.CompanyConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(Companies.CompanyConsts.CountryCodeMaxLength, MinimumLength = 2)]
    public string CountryCode { get; set; } = "TR";

    /// <summary>Merkez şirket mi? Tam bir satır HQ olmalı (base para birimi onboarding ülkesinden çözülür).</summary>
    public bool IsHeadquarters { get; set; }
}
