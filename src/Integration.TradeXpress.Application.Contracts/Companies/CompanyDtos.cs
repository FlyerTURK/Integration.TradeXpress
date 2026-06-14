using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
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

public class CompanyGetDto : EntityDto<Guid>, IGetDto<Guid>
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

    public int PageIndex { get; set; }
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
}
