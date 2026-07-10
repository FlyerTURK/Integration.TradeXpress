using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Commodities;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Metals;

/// <summary>Maden TEK temsili görseli — düzenleme modeli (paylaşılan <c>SingleImageEditFields</c> bileşenine
/// bağlanır). Kaynağı boş bırakılan görsel save'de temizlenir (<c>Metal.SetImage</c>).</summary>
public class MetalImageDto : ISingleImageEditModel
{
    public ProductImageSourceType SourceType { get; set; } = ProductImageSourceType.Url;

    [StringLength(MetalConsts.ImageUrlMaxLength)]
    public string? Url { get; set; }

    [StringLength(MetalConsts.ImageBlobNameMaxLength)]
    public string? BlobName { get; set; }

    [StringLength(MetalConsts.ImageFileNameMaxLength)]
    public string? FileName { get; set; }

    /// <summary>Blob görselin önizlemesi (data URL) — SALT görüntü; sunucu/upload doldurur, save yoksayar.</summary>
    public string? PreviewDataUrl { get; set; }
}

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

    /// <summary>Grid önizlemesi — Url tipinde doğrudan URL, Upload'da thumbnail data-URL'i (sunucu doldurur).</summary>
    public string? ImagePreviewUrl { get; set; }
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

    /// <summary>Temsili görsel (TEK) — kaynağı boşsa save'de temizlenir. Client binding için daima non-null başlar;
    /// sunucu Get'te de non-null garanti eder (EnrichGet).</summary>
    public MetalImageDto? Image { get; set; } = new();
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

    /// <summary>Temsili görsel (TEK) — kaynağı boşsa save'de temizlenir. Client binding için daima non-null başlar;
    /// sunucu Get'te de non-null garanti eder (EnrichGet).</summary>
    public MetalImageDto? Image { get; set; } = new();
}

public class MetalUpdateDto : FollowingUnitCatalogUpdateDtoBase
{
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

    /// <summary>Temsili görsel (TEK) — kaynağı boşsa save'de temizlenir. Client binding için daima non-null başlar;
    /// sunucu Get'te de non-null garanti eder (EnrichGet).</summary>
    public MetalImageDto? Image { get; set; } = new();
}
