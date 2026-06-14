using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Companies;

namespace Integration.TradeXpress.Blazor.Client.Pages.Companies.Models;

/// <summary>
/// Company düzenleme view-model'i. Base currency bir CurrencyUnit seçimidir. Şubeler (ve onların
/// kasaları) edit formunda drill list olarak in-memory düzenlenir; şirket kaydedilince tek
/// transaction'da (SaveTree) birlikte yazılır.
/// </summary>
public class CompanyViewModel : IViewModel<Guid>
{
    public Guid Id { get; set; }

    [Display(Name = "Code")]
    [Required]
    [StringLength(CompanyConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Name")]
    [Required]
    [StringLength(CompanyConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Country")]
    [Required]
    [StringLength(CompanyConsts.CountryCodeMaxLength, MinimumLength = 2)]
    public string CountryCode { get; set; } = string.Empty;

    [Required]
    public Guid BaseCurrencyUnitId { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsHeadquarters { get; set; }
    public int DisplayOrder { get; set; }

    [Display(Name = "Description")]
    [StringLength(CompanyConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public string? ConcurrencyStamp { get; set; }

    public List<BranchTreeItemViewModel> Branches { get; set; } = new();

    /// <summary>Kullanıcının drill'de kaldırdığı mevcut şubelerin sunucu Id'leri (SaveTree'de silinir).</summary>
    public List<Guid> DeletedBranchIds { get; set; } = new();
}
