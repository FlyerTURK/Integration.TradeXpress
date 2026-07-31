using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Commodities;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Metals;

public class MetalListRequestDto : ListRequestDto
{
}

/// <summary>Grid + süreç paneli picker'ı. İşçilik/sikke alanları panelin hesabı için taşınır.</summary>
public class MetalListDto : FollowingUnitCatalogListDtoBase
{
    public Guid? CompanyId { get; set; }
    public decimal Factor { get; set; }
    public bool FactorChange { get; set; }

    public bool IsQuantity { get; set; }
    public decimal StableQuantity { get; set; }

    public Guid? CostUnitId { get; set; }

    /// <summary>Grid önizlemesi — ana varyantın DAM poster URL'i (sunucu doldurur).</summary>
    public string? ImagePreviewUrl { get; set; }

    // -- Varyanttan türetilen işçilik alanları --
    public MetalLaborType LaborType { get; set; }
    public decimal EntryLabor { get; set; }
    public Guid? EntryLaborUnitId { get; set; }
    public bool EntryLaborChange { get; set; }
    public decimal ExitLabor { get; set; }
    public Guid? ExitLaborUnitId { get; set; }
    public bool ExitLaborChange { get; set; }
}

public class MetalGetDto : FollowingUnitCatalogGetDtoBase, IHasCode
{
    public Guid? CompanyId { get; set; }

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

    public Guid? CostUnitId { get; set; }

    [StringLength(MetalConsts.BarcodeMaxLength)]
    public string? Barcode { get; set; }
    [StringLength(MetalConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // ── Agnostik graf (in-memory; kayıtta AppService persist eder). Görseller VARYANT seviyesinde yönetilir
    //    (EntityVariantsPanel ShowImages); burada Doküman/Not + Nitelik/Varyant grafı taşınır. ──
    public List<EntityDocumentEditDto> Documents { get; set; } = new();
    public List<EntityNoteEditDto> Notes { get; set; } = new();
    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
    public List<MetalVariantGraphDto> Variants { get; set; } = new();
}

public class MetalCreateDto : FollowingUnitCatalogCreateDtoBase
{
    public Guid? CompanyId { get; set; }

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

    public Guid? CostUnitId { get; set; }

    [StringLength(MetalConsts.BarcodeMaxLength)]
    public string? Barcode { get; set; }
    [StringLength(MetalConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<EntityDocumentEditDto> Documents { get; set; } = new();
    public List<EntityNoteEditDto> Notes { get; set; } = new();
    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
    public List<MetalVariantGraphDto> Variants { get; set; } = new();
}

public class MetalUpdateDto : FollowingUnitCatalogUpdateDtoBase
{
    public Guid? CompanyId { get; set; }

    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04: host CurrencyUnit kayıtları dışında tüm kodlar değiştirilebilir).
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

    public Guid? CostUnitId { get; set; }

    [StringLength(MetalConsts.BarcodeMaxLength)]
    public string? Barcode { get; set; }
    [StringLength(MetalConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<EntityDocumentEditDto> Documents { get; set; } = new();
    public List<EntityNoteEditDto> Notes { get; set; } = new();
    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
    public List<MetalVariantGraphDto> Variants { get; set; } = new();
}

public class MetalVariantGraphDto : EntityVariantGraphDto
{
    public MetalLaborType LaborType { get; set; }
    public decimal EntryLabor { get; set; }
    public Guid? EntryLaborUnitId { get; set; }
    public decimal ExitLabor { get; set; }
    public Guid? ExitLaborUnitId { get; set; }
}

