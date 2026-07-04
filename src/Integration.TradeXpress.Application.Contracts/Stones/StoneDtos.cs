using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Stones;

public class StoneListRequestDto : ListRequestDto
{
    /// <summary>Çalışılan şirket — görünür kayıtlar host(null) + bu şirkete-özel olanlar.</summary>
    public Guid? CompanyId { get; set; }
}

public class StoneListDto : CatalogListDtoBase, IPricedCommodityListDto
{
    public string? StoneKind { get; set; }
    public string? Color { get; set; }

    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; }
    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    public decimal ExitPrice { get; set; }
    public Guid? ExitPriceUnitId { get; set; }
}

public class StoneGetDto : CatalogGetDtoBase, IHasCode
{
    [Required]
    [StringLength(StoneConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(StoneConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

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
}

public class StoneCreateDto : CatalogCreateDtoBase
{
    /// <summary>Sahip şirket — client çalışılan şirketi atar (otomatik scope).</summary>
    public Guid? CompanyId { get; set; }

    [Required]
    [StringLength(StoneConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(StoneConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

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

public class StoneUpdateDto : CatalogUpdateDtoBase
{
    [Required]
    [StringLength(StoneConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

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
