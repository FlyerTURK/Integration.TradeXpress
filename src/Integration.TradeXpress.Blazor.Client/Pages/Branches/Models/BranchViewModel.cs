using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Branches;

namespace Integration.TradeXpress.Blazor.Client.Pages.Branches.Models;

/// <summary>Branch (şube) düzenleme view-model'i. Parent şirket bir Company seçimidir (yeni kayıtta).</summary>
public class BranchViewModel : IViewModel<Guid>
{
    public Guid Id { get; set; }

    [Display(Name = "Entity:Company")]
    [Required]
    public Guid CompanyId { get; set; }

    [Display(Name = "Name")]
    [Required]
    [StringLength(BranchConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsHeadquarters { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    [Display(Name = "Description")]
    [StringLength(BranchConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}
