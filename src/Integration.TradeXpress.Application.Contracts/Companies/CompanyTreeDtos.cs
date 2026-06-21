using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Vaults;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Companies;

// ── Okuma: şirket + şubeler + kasalar (düzenleme için tam ağaç). Kasa düzenleme UI'ı KARDEŞ
//    popup'tadır (şube edit formuna gömülü drill DEĞİL — iç içe EditContext render NRE'si verir). ──

public class CompanyTreeDto : EntityDto<Guid>
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public Guid BaseCurrencyUnitId { get; set; }
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsHeadquarters { get; set; }
    public int DisplayOrder { get; set; }
    public string? Description { get; set; }
    public string? ConcurrencyStamp { get; set; }

    public List<BranchTreeDto> Branches { get; set; } = new();
}

public class BranchTreeDto : EntityDto<Guid>
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsHeadquarters { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string? Description { get; set; }
    public string? ConcurrencyStamp { get; set; }

    public List<VaultTreeDto> Vaults { get; set; } = new();
}

public class VaultTreeDto : EntityDto<Guid>
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string? Description { get; set; }
    public string? ConcurrencyStamp { get; set; }
}

// ── Yazma: şirket + şubeler + kasalar tek transaction'da diff'lenip kaydedilir (in-memory commit) ──
// Id boş/null => yeni kayıt; dolu => mevcut. Girdide olmayan mevcut çocuklar (yalnız açıkça
// kaldırılanlar) silinir. Kasa düzenleme KARDEŞ popup üzerinden yapılır (gömülü drill değil).

public class CompanyTreeSaveDto
{
    public Guid? Id { get; set; }

    [Required]
    [StringLength(CompanyConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(CompanyConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(CompanyConsts.CountryCodeMaxLength, MinimumLength = 2)]
    public string CountryCode { get; set; } = string.Empty;

    [Required]
    public Guid BaseCurrencyUnitId { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsHeadquarters { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(CompanyConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public string? ConcurrencyStamp { get; set; }

    public List<BranchTreeSaveDto> Branches { get; set; } = new();

    /// <summary>Kullanıcının AÇIKÇA kaldırdığı mevcut şubelerin Id'leri. Yalnız bunlar silinir
    /// (kör omission-delete YOK) — böylece eşzamanlı eklenen şubeler korunur.</summary>
    public List<Guid> DeletedBranchIds { get; set; } = new();
}

public class BranchTreeSaveDto
{
    public Guid? Id { get; set; }

    [Required]
    [StringLength(BranchConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(BranchConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsHeadquarters { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    [StringLength(BranchConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public string? ConcurrencyStamp { get; set; }

    public List<VaultTreeSaveDto> Vaults { get; set; } = new();

    /// <summary>Kullanıcının açıkça kaldırdığı mevcut kasaların Id'leri (yalnız bunlar silinir).</summary>
    public List<Guid> DeletedVaultIds { get; set; } = new();
}

public class VaultTreeSaveDto
{
    public Guid? Id { get; set; }

    [Required]
    [StringLength(VaultConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(VaultConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    [StringLength(VaultConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public string? ConcurrencyStamp { get; set; }
}
