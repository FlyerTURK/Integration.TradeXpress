using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.AssayOffices;

/// <summary>AssayOffice liste sorgusu (per-tenant). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class AssayOfficeListRequestDto : ListRequestDto
{
}

public class AssayOfficeListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}

public class AssayOfficeGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    [Required]
    [StringLength(AssayOfficeConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(AssayOfficeConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(AssayOfficeConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class AssayOfficeCreateDto : ICreateDto
{
    [Required]
    [StringLength(AssayOfficeConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(AssayOfficeConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    [StringLength(AssayOfficeConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class AssayOfficeUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(AssayOfficeConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(AssayOfficeConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(AssayOfficeConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}
