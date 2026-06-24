using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Cashes;

/// <summary>Cash listesi sorgusu (host kataloğu + tenant'ın kendileri).</summary>
public class CashListRequestDto : ListRequestDto
{
}

public class CashListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive, IHostScoped
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Takip edilen para birimi (cins) — FK.</summary>
    public Guid FollowingUnitId { get; set; }
    /// <summary>Takip edilen para biriminin kodu (link/gösterim; self-join ile çözülür).</summary>
    public string? FollowingUnitCode { get; set; }
    /// <summary>Takip edilen para biriminin adı (gösterim).</summary>
    public string? FollowingUnitName { get; set; }

    public bool IsActive { get; set; }
    /// <summary>Host kataloğu (TenantId=null) mu? Tenant bunu düzenleyemez; salt-okur.</summary>
    public bool IsGlobal { get; set; }
}

public class CashGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    // Client validasyonu modelin ÜZERİNDE (agnostic Form); server-input doğrulaması Create/Update DTO'larında.
    [Required]
    [StringLength(CashConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(CashConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? FollowingUnitId { get; set; }
    public string? FollowingUnitCode { get; set; }

    [StringLength(CashConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public class CashCreateDto : ICreateDto
{
    [Required]
    [StringLength(CashConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(CashConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? FollowingUnitId { get; set; }

    [StringLength(CashConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class CashUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(CashConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? FollowingUnitId { get; set; }

    [StringLength(CashConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
}
