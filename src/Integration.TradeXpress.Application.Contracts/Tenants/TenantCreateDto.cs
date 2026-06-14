using Integration.Framework.Base.Dtos.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace Integration.TradeXpress.Tenants;

public class TenantCreateDto : ICreateDto
{
    [Required]
    [StringLength(64)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string AdminEmailAddress { get; set; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string AdminPassword { get; set; } = string.Empty;

    // ── Merkez (HQ) şirket onboarding ─────────────────────────────────────────
    // Doluysa yeni tenant'ın HQ şirketi bu bilgilerle kurulur (ülkeye göre base para
    // otomatik). Boşsa seed varsayılan HQ'yu (Merkez/TR/TRY) kurar.
    [StringLength(Companies.CompanyConsts.NameMaxLength)]
    public string? HqCompanyName { get; set; }

    [StringLength(Companies.CompanyConsts.CountryCodeMaxLength, MinimumLength = 2)]
    public string? HqCountryCode { get; set; }
}
