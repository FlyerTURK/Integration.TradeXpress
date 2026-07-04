using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Commodities;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Metals;

public class MetalListRequestDto : ListRequestDto
{
}

/// <summary>Grid + süreç paneli picker'ı. İşçilik/sikke alanları panelin hesabı için taşınır.</summary>
public class MetalListDto : FollowingUnitCatalogListDtoBase
{
    public decimal Factor { get; set; }
    public bool FactorChange { get; set; }

    public bool IsQuantity { get; set; }
    public decimal StableQuantity { get; set; }

    public MetalLaborType LaborType { get; set; }
    public bool LaborTypeChange { get; set; }
    public decimal EntryLabor { get; set; }
    public Guid? EntryLaborUnitId { get; set; }
    public bool EntryLaborChange { get; set; }
    public decimal ExitLabor { get; set; }
    public Guid? ExitLaborUnitId { get; set; }
    public bool ExitLaborChange { get; set; }
    public Guid? CostUnitId { get; set; }
}

public class MetalGetDto : FollowingUnitCatalogGetDtoBase, IHasCode
{
    [Required]
    [StringLength(MetalConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(MetalConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [Range(0.0000001, double.MaxValue)]
    public decimal Factor { get; set; } = MetalConsts.DefaultFactor;
    public bool FactorChange { get; set; }

    public bool IsQuantity { get; set; }
    public decimal StableQuantity { get; set; }

    public MetalLaborType LaborType { get; set; }
    public bool LaborTypeChange { get; set; }
    public decimal EntryLabor { get; set; }
    public Guid? EntryLaborUnitId { get; set; }
    public bool EntryLaborChange { get; set; }
    public decimal ExitLabor { get; set; }
    public Guid? ExitLaborUnitId { get; set; }
    public bool ExitLaborChange { get; set; }
    public Guid? CostUnitId { get; set; }

    [StringLength(MetalConsts.BarcodeMaxLength)]
    public string? Barcode { get; set; }
    [StringLength(MetalConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class MetalCreateDto : FollowingUnitCatalogCreateDtoBase
{
    [Required]
    [StringLength(MetalConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(MetalConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [Range(0.0000001, double.MaxValue)]
    public decimal Factor { get; set; } = MetalConsts.DefaultFactor;
    public bool FactorChange { get; set; }

    public bool IsQuantity { get; set; }
    public decimal StableQuantity { get; set; }

    public MetalLaborType LaborType { get; set; }
    public bool LaborTypeChange { get; set; }
    public decimal EntryLabor { get; set; }
    public Guid? EntryLaborUnitId { get; set; }
    public bool EntryLaborChange { get; set; }
    public decimal ExitLabor { get; set; }
    public Guid? ExitLaborUnitId { get; set; }
    public bool ExitLaborChange { get; set; }
    public Guid? CostUnitId { get; set; }

    [StringLength(MetalConsts.BarcodeMaxLength)]
    public string? Barcode { get; set; }
    [StringLength(MetalConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class MetalUpdateDto : FollowingUnitCatalogUpdateDtoBase
{
    [Required]
    [StringLength(MetalConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [Range(0.0000001, double.MaxValue)]
    public decimal Factor { get; set; } = MetalConsts.DefaultFactor;
    public bool FactorChange { get; set; }

    public bool IsQuantity { get; set; }
    public decimal StableQuantity { get; set; }

    public MetalLaborType LaborType { get; set; }
    public bool LaborTypeChange { get; set; }
    public decimal EntryLabor { get; set; }
    public Guid? EntryLaborUnitId { get; set; }
    public bool EntryLaborChange { get; set; }
    public decimal ExitLabor { get; set; }
    public Guid? ExitLaborUnitId { get; set; }
    public bool ExitLaborChange { get; set; }
    public Guid? CostUnitId { get; set; }

    [StringLength(MetalConsts.BarcodeMaxLength)]
    public string? Barcode { get; set; }
    [StringLength(MetalConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}
