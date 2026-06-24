using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Metals;

public class MetalListRequestDto : ListRequestDto
{
}

/// <summary>Grid + süreç paneli picker'ı. İşçilik/sikke alanları panelin hesabı için taşınır.</summary>
public class MetalListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive, IHostScoped
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Guid FollowingUnitId { get; set; }
    public string? FollowingUnitCode { get; set; }
    public decimal Purity { get; set; }
    public bool PurityChange { get; set; }

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

    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public class MetalGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(MetalConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(MetalConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? FollowingUnitId { get; set; }
    public string? FollowingUnitCode { get; set; }

    [Range(0.0000001, double.MaxValue)]
    public decimal Purity { get; set; } = MetalConsts.DefaultPurity;
    public bool PurityChange { get; set; }

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

    public bool IsActive { get; set; }
    public bool IsGlobal { get; set; }
}

public class MetalCreateDto : ICreateDto
{
    [Required]
    [StringLength(MetalConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(MetalConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? FollowingUnitId { get; set; }

    [Range(0.0000001, double.MaxValue)]
    public decimal Purity { get; set; } = MetalConsts.DefaultPurity;
    public bool PurityChange { get; set; }

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

public class MetalUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(MetalConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? FollowingUnitId { get; set; }

    [Range(0.0000001, double.MaxValue)]
    public decimal Purity { get; set; } = MetalConsts.DefaultPurity;
    public bool PurityChange { get; set; }

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

    public bool IsActive { get; set; }
}
