using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Products;

/// <summary>Product liste sorgusu (per-tenant). Company-scoped: sunucu <see cref="ICurrentCompany"/> ile daraltır
/// (client CompanyId GÖNDERMEZ — AssayOffice deseni). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class ProductListRequestDto : ListRequestDto
{
}

public class ProductListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>Ürüne bağlı (silinmemiş) varyant sayısı — grid göstergesi.</summary>
    public int VariantCount { get; set; }
}

public class ProductGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(ProductConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ProductConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ProductConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Varyantlar (graf düğümleri; Id + IsDeleted ile diff). Product edit formundaki drill yönetir.</summary>
    public List<ProductVariantGraphDto> Variants { get; set; } = new();
}

public class ProductCreateDto : ICreateDto
{
    [Required]
    [StringLength(ProductConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ProductConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ProductConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<ProductVariantGraphDto> Variants { get; set; } = new();
}

public class ProductUpdateDto : IUpdateDto
{
    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04). Scope'lu benzersizlik AppService'te.
    [Required]
    [StringLength(ProductConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ProductConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ProductConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public List<ProductVariantGraphDto> Variants { get; set; } = new();
}

/// <summary>
/// Product grafının varyant DÜĞÜMÜ — Product edit'inde in-memory drill + Product save'i içindir (SubAccountGraphDto
/// deseni). Durum = <see cref="Id"/> + <see cref="IsDeleted"/>: Id boş → ekle, IsDeleted → sil, aksi → güncelle.
/// <see cref="IsMain"/> DISPLAY-ONLY (ana varyant değişmezi <c>ProductVariantManager</c>'da; Adım 1'de UI'dan seçilmez).
/// </summary>
public class ProductVariantGraphDto
{
    public Guid Id { get; set; }
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    /// <summary>Ana (main) varyant mı — DISPLAY-ONLY göstergesi (manager yönetir; drill'de düzenlenmez).</summary>
    public bool IsMain { get; set; }

    [Required]
    [StringLength(ProductConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ProductConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ProductConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}
