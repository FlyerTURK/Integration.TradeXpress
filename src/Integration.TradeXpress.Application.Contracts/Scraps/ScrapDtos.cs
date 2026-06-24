using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Scraps;

public class ScrapListRequestDto : ListRequestDto
{
}

public class ScrapListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive, IHostScoped
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Guid FollowingUnitId { get; set; }
    public string? FollowingUnitCode { get; set; }
    public decimal Purity { get; set; }
    public bool PurityChange { get; set; }

    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public class ScrapGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(ScrapConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ScrapConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? FollowingUnitId { get; set; }
    public string? FollowingUnitCode { get; set; }

    [Range(0.0000001, 1.0)]
    public decimal Purity { get; set; } = ScrapConsts.DefaultPurity;

    public bool PurityChange { get; set; } = true;

    [StringLength(ScrapConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public class ScrapCreateDto : ICreateDto
{
    [Required]
    [StringLength(ScrapConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ScrapConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? FollowingUnitId { get; set; }

    [Range(0.0000001, 1.0)]
    public decimal Purity { get; set; } = ScrapConsts.DefaultPurity;

    public bool PurityChange { get; set; } = true;

    [StringLength(ScrapConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class ScrapUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(ScrapConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? FollowingUnitId { get; set; }

    [Range(0.0000001, 1.0)]
    public decimal Purity { get; set; } = ScrapConsts.DefaultPurity;

    public bool PurityChange { get; set; } = true;

    [StringLength(ScrapConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
}
