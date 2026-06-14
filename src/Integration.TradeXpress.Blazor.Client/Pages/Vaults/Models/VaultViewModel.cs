using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Vaults;

namespace Integration.TradeXpress.Blazor.Client.Pages.Vaults.Models;

/// <summary>Vault (kasa) düzenleme view-model'i. Parent şube bir Branch seçimidir (yeni kayıtta).</summary>
public class VaultViewModel : IViewModel<Guid>
{
    public Guid Id { get; set; }

    [Display(Name = "Entity:Branch")]
    [Required]
    public Guid BranchId { get; set; }

    [Display(Name = "Name")]
    [Required]
    [StringLength(VaultConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    [Display(Name = "Description")]
    [StringLength(VaultConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}
