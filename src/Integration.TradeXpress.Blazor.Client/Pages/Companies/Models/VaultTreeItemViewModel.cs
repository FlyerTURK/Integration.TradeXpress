using System;
using System.ComponentModel.DataAnnotations;
using Integration.TradeXpress.Vaults;

namespace Integration.TradeXpress.Blazor.Client.Pages.Companies.Models;

/// <summary>Şube ağacındaki bir kasa (in-memory drill öğesi). <see cref="Id"/> null = yeni.</summary>
public class VaultTreeItemViewModel
{
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public Guid? Id { get; set; }

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

    public string? ConcurrencyStamp { get; set; }
}
