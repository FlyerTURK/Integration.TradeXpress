using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Commodities;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Futures;

public class FutureListRequestDto : ListRequestDto
{
}

public class FutureListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive, IHostScoped, IFollowingUnitDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Guid FollowingUnitId { get; set; }
    public string? FollowingUnitCode { get; set; }
    public decimal FollowingFactor { get; set; }

    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public class FutureGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode, IHostScoped, IFollowingUnitDto
{
    [Required]
    [StringLength(FutureConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(FutureConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? FollowingUnitId { get; set; }
    public string? FollowingUnitCode { get; set; }

    [Range(0.0000001, double.MaxValue)]
    public decimal FollowingFactor { get; set; } = 1m;

    [StringLength(FutureConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public class FutureCreateDto : ICreateDto
{
    [Required]
    [StringLength(FutureConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(FutureConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? FollowingUnitId { get; set; }

    [Range(0.0000001, double.MaxValue)]
    public decimal FollowingFactor { get; set; } = 1m;

    [StringLength(FutureConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class FutureUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(FutureConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? FollowingUnitId { get; set; }

    [Range(0.0000001, double.MaxValue)]
    public decimal FollowingFactor { get; set; } = 1m;

    [StringLength(FutureConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
}
