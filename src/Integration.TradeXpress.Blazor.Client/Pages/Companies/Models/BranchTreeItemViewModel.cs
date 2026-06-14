using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Branches;

namespace Integration.TradeXpress.Blazor.Client.Pages.Companies.Models;

/// <summary>
/// Şirket ağacındaki bir şube (in-memory drill öğesi). <see cref="ClientKey"/> grid/identity için
/// daima dolu; <see cref="Id"/> sunucu kimliğidir ve yeni öğelerde null (SaveTree yeni sayar).
/// </summary>
public class BranchTreeItemViewModel : IViewModel<Guid>
{
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public Guid? Id { get; set; }

    // CrudPageBase<..., BranchTreeItemViewModel> için — mevcut Guid? Id property'yi kırmaz.
    Guid IViewModel<Guid>.Id
    {
        get => Id ?? Guid.Empty;
        set => Id = value == Guid.Empty ? null : value;
    }

    [Display(Name = "Code")]
    [Required]
    [StringLength(BranchConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

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

    public string? ConcurrencyStamp { get; set; }

    /// <summary>Şubenin kasaları (in-memory). KARDEŞ popup'ta düzenlenir — şube edit formunun
    /// EditForm'u İÇİNE gömülü drill KOYULMAZ (iç içe EditContext render NRE'si verir).</summary>
    public List<VaultTreeItemViewModel> Vaults { get; set; } = new();

    /// <summary>Kullanıcının kaldırdığı mevcut kasaların sunucu Id'leri (SaveTree'de silinir).</summary>
    public List<Guid> DeletedVaultIds { get; set; } = new();
}
