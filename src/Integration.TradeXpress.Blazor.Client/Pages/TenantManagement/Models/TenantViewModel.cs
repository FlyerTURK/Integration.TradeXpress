using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Blazor.Client.Components.Crud;
using Microsoft.Extensions.Localization;

namespace Integration.TradeXpress.Blazor.Client.Pages.TenantManagement.Models;

public class TenantViewModel : IViewModel<Guid>, IValidatableObject
{
    public Guid Id { get; set; }

    [Display(Name = "Name")]
    [Required]
    [StringLength(64)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "AdminEmailAddress")]
    [EmailAddress]
    public string AdminEmailAddress { get; set; } = string.Empty;

    [Display(Name = "AdminPassword")]
    [StringLength(128)]
    public string AdminPassword { get; set; } = string.Empty;

    // Merkez (HQ) şirket — yalnız yeni tenant oluştururken.
    [Display(Name = "HqCompanyName")]
    [StringLength(128)]
    public string? HqCompanyName { get; set; }
    public string? HqCountryCode { get; set; }

    // Yönetici e-posta/şifre yalnız YENİ tenant oluştururken zorunludur; aynı VM düzenleme
    // modunda da kullanıldığı için (bu alanlar gizli) koşullu doğrulanır.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Id == Guid.Empty)
        {
            var L = validationContext.GetService(
                typeof(IStringLocalizer<Integration.TradeXpress.Localization.TradeXpressResource>)) as IStringLocalizer;

            if (string.IsNullOrWhiteSpace(AdminEmailAddress))
            {
                var msg = L != null
                    ? L["Validation:Required", L["AdminEmailAddress"]].Value
                    : "Yönetici e-posta adresi zorunludur.";
                yield return new ValidationResult(msg, new[] { nameof(AdminEmailAddress) });
            }
            if (string.IsNullOrWhiteSpace(AdminPassword))
            {
                var msg = L != null
                    ? L["Validation:Required", L["AdminPassword"]].Value
                    : "Yönetici şifresi zorunludur.";
                yield return new ValidationResult(msg, new[] { nameof(AdminPassword) });
            }
        }
    }
}
