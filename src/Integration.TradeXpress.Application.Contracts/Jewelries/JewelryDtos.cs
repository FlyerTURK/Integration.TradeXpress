using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Jewelries;

public class JewelryListRequestDto : ListRequestDto
{
    /// <summary>Çalışılan şirket — görünür kayıtlar host/holding-host(null) + bu şirkete-özel olanlar.</summary>
    public Guid? CompanyId { get; set; }
}

public class JewelryListDto : CatalogListDtoBase, IPricedCommodityListDto
{
    public string? Model { get; set; }
    public string? Kind { get; set; }

    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; }
    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    public decimal ExitPrice { get; set; }
    public Guid? ExitPriceUnitId { get; set; }

    public Guid? CompanyId { get; set; }
}

public class JewelryGetDto : CatalogGetDtoBase, IHasCode
{
    [Required]
    [StringLength(JewelryConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(JewelryConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [StringLength(JewelryConsts.AttributeMaxLength)] public string? Model { get; set; }
    [StringLength(JewelryConsts.AttributeMaxLength)] public string? Kind { get; set; }
    [StringLength(JewelryConsts.AttributeMaxLength)] public string? Type { get; set; }
    [StringLength(JewelryConsts.AttributeMaxLength)] public string? Color { get; set; }
    [StringLength(JewelryConsts.AttributeMaxLength)] public string? Category { get; set; }
    [StringLength(JewelryConsts.AttributeMaxLength)] public string? GroupCode { get; set; }

    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; } = true;
    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    public decimal ExitPrice { get; set; }
    public Guid? ExitPriceUnitId { get; set; }

    [StringLength(JewelryConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public Guid? CompanyId { get; set; }
}

public class JewelryCreateDto : CatalogCreateDtoBase
{
    /// <summary>Sahip şirket — client çalışılan şirketi atar (otomatik scope).</summary>
    public Guid? CompanyId { get; set; }

    [Required]
    [StringLength(JewelryConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(JewelryConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    public string? Model { get; set; }
    public string? Kind { get; set; }
    public string? Type { get; set; }
    public string? Color { get; set; }
    public string? Category { get; set; }
    public string? GroupCode { get; set; }

    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; } = true;
    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    public decimal ExitPrice { get; set; }
    public Guid? ExitPriceUnitId { get; set; }

    [StringLength(JewelryConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class JewelryUpdateDto : CatalogUpdateDtoBase
{
    [Required]
    [StringLength(JewelryConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    public string? Model { get; set; }
    public string? Kind { get; set; }
    public string? Type { get; set; }
    public string? Color { get; set; }
    public string? Category { get; set; }
    public string? GroupCode { get; set; }

    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }
    public bool PriceTypeChange { get; set; } = true;
    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    public decimal ExitPrice { get; set; }
    public Guid? ExitPriceUnitId { get; set; }

    [StringLength(JewelryConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}
