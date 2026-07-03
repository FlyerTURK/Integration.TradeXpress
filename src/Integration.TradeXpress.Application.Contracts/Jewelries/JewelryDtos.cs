using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Jewelries;

public class JewelryListRequestDto : ListRequestDto
{
    /// <summary>Çalışılan şirket — görünür kayıtlar host/holding-host(null) + bu şirkete-özel olanlar.</summary>
    public Guid? CompanyId { get; set; }
}

public class JewelryListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive, IHostScoped, IPricedCommodityListDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

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
    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public class JewelryGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(JewelryConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(JewelryConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

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
    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public class JewelryCreateDto : ICreateDto
{
    /// <summary>Sahip şirket — client çalışılan şirketi atar (otomatik scope).</summary>
    public Guid? CompanyId { get; set; }

    [Required]
    [StringLength(JewelryConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(JewelryConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

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

public class JewelryUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(JewelryConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

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

    public bool IsActive { get; set; }
}
