using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.AddOns;

/// <summary>AddOn liste sorgusu (per-tenant, company-owned). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class AddOnListRequestDto : ListRequestDto
{
}

public class AddOnListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public Guid CurrencyUnitId { get; set; }

    /// <summary>Para birimi kodu — sunucu enrich eder (grid gösterimi; entity id-only tuttuğundan mapper doldurmaz).</summary>
    public string? CurrencyUnitCode { get; set; }

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}

public class AddOnGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    [Required]
    [StringLength(AddOnConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(AddOnConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public Guid CurrencyUnitId { get; set; }

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(AddOnConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class AddOnCreateDto : ICreateDto
{
    [Required]
    [StringLength(AddOnConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(AddOnConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public Guid CurrencyUnitId { get; set; }

    public int DisplayOrder { get; set; }

    [StringLength(AddOnConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class AddOnUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(AddOnConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(AddOnConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public Guid CurrencyUnitId { get; set; }

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(AddOnConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}
