using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Services;

/// <summary>Service listesi sorgusu (host kataloğu + tenant'ın kendileri).</summary>
public class ServiceListRequestDto : ListRequestDto
{
}

public class ServiceListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive, IHostScoped
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    /// <summary>Host kataloğu (TenantId=null) mu? Tenant bunu düzenleyemez; salt-okur.</summary>
    public bool IsGlobal { get; set; }
}

public class ServiceGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode, IHostScoped
{
    [Required]
    [StringLength(ServiceConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ServiceConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ServiceConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public class ServiceCreateDto : ICreateDto
{
    [Required]
    [StringLength(ServiceConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ServiceConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ServiceConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class ServiceUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(ServiceConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ServiceConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
}
