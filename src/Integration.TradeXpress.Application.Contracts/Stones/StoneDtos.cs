using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Stones;

public class StoneListRequestDto : ListRequestDto
{
    /// <summary>Çalışılan şirket — görünür kayıtlar host(null) + bu şirkete-özel olanlar.</summary>
    public Guid? CompanyId { get; set; }
}

public class StoneListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive, IHostScoped, IPricedCommodityListDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public string? StoneKind { get; set; }
    public string? Color { get; set; }

    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; }
    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    public decimal ExitPrice { get; set; }
    public Guid? ExitPriceUnitId { get; set; }

    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public class StoneGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode, IHostScoped
{
    [Required]
    [StringLength(StoneConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(StoneConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(StoneConsts.AttributeMaxLength)] public string? StoneKind { get; set; }
    [StringLength(StoneConsts.AttributeMaxLength)] public string? StoneType { get; set; }
    [StringLength(StoneConsts.AttributeMaxLength)] public string? Color { get; set; }
    [StringLength(StoneConsts.AttributeMaxLength)] public string? Cut { get; set; }
    [StringLength(StoneConsts.AttributeMaxLength)] public string? Clarity { get; set; }
    [StringLength(StoneConsts.AttributeMaxLength)] public string? Sieve { get; set; }
    [StringLength(StoneConsts.AttributeMaxLength)] public string? Category { get; set; }
    [StringLength(StoneConsts.AttributeMaxLength)] public string? GroupCode { get; set; }

    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; } = true;
    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    public decimal ExitPrice { get; set; }
    public Guid? ExitPriceUnitId { get; set; }

    [StringLength(StoneConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public Guid? CompanyId { get; set; }

    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public class StoneCreateDto : ICreateDto
{
    /// <summary>Sahip şirket — client çalışılan şirketi atar (otomatik scope).</summary>
    public Guid? CompanyId { get; set; }

    [Required]
    [StringLength(StoneConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(StoneConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    public string? StoneKind { get; set; }
    public string? StoneType { get; set; }
    public string? Color { get; set; }
    public string? Cut { get; set; }
    public string? Clarity { get; set; }
    public string? Sieve { get; set; }
    public string? Category { get; set; }
    public string? GroupCode { get; set; }

    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; } = true;
    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    public decimal ExitPrice { get; set; }
    public Guid? ExitPriceUnitId { get; set; }

    [StringLength(StoneConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class StoneUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(StoneConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    public string? StoneKind { get; set; }
    public string? StoneType { get; set; }
    public string? Color { get; set; }
    public string? Cut { get; set; }
    public string? Clarity { get; set; }
    public string? Sieve { get; set; }
    public string? Category { get; set; }
    public string? GroupCode { get; set; }

    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; } = true;
    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    public decimal ExitPrice { get; set; }
    public Guid? ExitPriceUnitId { get; set; }

    [StringLength(StoneConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
}
