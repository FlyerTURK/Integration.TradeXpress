using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Branches;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Companies;

/// <summary>Company liste sorgusu (per-tenant). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class CompanyListRequestDto : ListRequestDto
{
}

public class CompanyListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public Guid BaseCurrencyUnitId { get; set; }
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsHeadquarters { get; set; }
    public int DisplayOrder { get; set; }
}

public class CompanyGetDto : EntityDto<Guid>, IGetDto<Guid>, ICompanyGraph
{
    // VALİDASYON kuralları BURADA (tek kaynak) — CompanyGraphDto bunlardan MİRAS alır → standalone ve
    // tenant-node düzenlemeleri GARANTİLİ aynı kuralları doğrular (ıraksayamaz).
    [Required]
    [StringLength(CompanyConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(CompanyConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(CompanyConsts.CountryCodeMaxLength, MinimumLength = 2)]
    public string CountryCode { get; set; } = string.Empty;

    public Guid BaseCurrencyUnitId { get; set; }
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsHeadquarters { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(CompanyConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Sahip olunan şubeler (graf düğümleri; durum = Id + IsDeleted). Edit formu in-memory yönetir;
    // Create/Update tek komutta BranchAppService'e delege eder.
    public List<BranchGraphDto> Branches { get; set; } = new();
}

/// <summary>
/// Tenant onboarding'inin şirket DÜĞÜMÜ — tenant edit'inde in-memory drill + tenant save'i içindir
/// (kendi app servisi YOK; standalone Company CRUD ayrı: <see cref="CompanyGetDto"/> vb.). Durum =
/// <see cref="Id"/> + <see cref="IsDeleted"/>; şubeler <see cref="Branches"/> (şube→kasa grafı).
/// </summary>
public class CompanyGraphDto : CompanyGetDto
{
    // Graf düğümü EKSTRALARI (durum). Code/Name/CountryCode/Branches + TÜM VALİDASYON CompanyGetDto'dan
    // MİRAS → standalone ve tenant-node şirket düzenlemeleri tek kaynaktan, GARANTİLİ aynı (kopya yok). (K3: GraphDto : GetDto)
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }
}

public class CompanyCreateDto : ICreateDto
{
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

    public bool IsHeadquarters { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(CompanyConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Sahip olunan şubeler (graf) — tek komutta yazılır (BranchAppService'e delege).
    public List<BranchGraphDto> Branches { get; set; } = new();
}

public class CompanyUpdateDto : IUpdateDto
{
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

    public bool IsActive { get; set; }
    public bool IsHeadquarters { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(CompanyConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Sahip olunan şubeler (graf; Id+IsDeleted ile diff) — tek komutta yazılır (BranchAppService'e delege).
    public List<BranchGraphDto> Branches { get; set; } = new();
}
