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

    /// <summary>Nitelikler (varyant eksenleri; değerleriyle birlikte graf). Varyantlar bunların
    /// kartezyeninden sunucuda ÜRETİLİR (ProductVariantSynchronizer).</summary>
    public List<ProductAttributeGraphDto> Attributes { get; set; } = new();
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

    /// <summary>Nitelik grafı — bkz. <see cref="ProductGetDto.Attributes"/>.</summary>
    public List<ProductAttributeGraphDto> Attributes { get; set; } = new();
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

    /// <summary>Nitelik grafı — bkz. <see cref="ProductGetDto.Attributes"/>.</summary>
    public List<ProductAttributeGraphDto> Attributes { get; set; } = new();
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

    /// <summary>Varyantın nitelik-değer KOMBİNASYON özeti (ör. "Kırmızı / M") — SALT-OKUNUR görüntü alanı.
    /// GetAsync projeksiyonunda doldurulur (attribute DisplayOrder sırasıyla " / " join); save'de YOKSAYILIR.</summary>
    public string AttributeSummary { get; set; } = string.Empty;

    /// <summary>Kombinasyonun İSTEMCİ-taraflı kimliği — ilgili DEĞERLERİN <see cref="ProductAttributeValueGraphDto.ClientKey"/>'lerinin
    /// sıralı "|" join'i. <c>GenerateVariantsAsync</c> doldurur, client round-trip eder; kayıtta Id'siz (henüz DB'de olmayan)
    /// üretilmiş satırın özelleştirmelerini (Code/Name/Description/IsActive) senkron sonrası DB varyantına EŞLEMEK içindir.</summary>
    public string CombinationKey { get; set; } = string.Empty;
}

/// <summary>Persistsiz varyant üretim isteği (önizleme): nitelik grafı + ad türetmesi için ürün adı.
/// DB'ye YAZMAZ — kartezyen hesaplanır, varyant graf satırları döner (kalıcılaşma Product save'inde).</summary>
public class ProductVariantGenerateRequestDto
{
    /// <summary>Varyant AD türetmesi için ürün adı ("Ürün Kırmızı M") — synchronizer paritesi. Boşsa yalnız değer adları.</summary>
    public string? ProductName { get; set; }

    public List<ProductAttributeGraphDto> Attributes { get; set; } = new();
}

/// <summary>
/// Product grafının NİTELİK düğümü — varyant ekseni (ör. "Renk", "Beden"), değerleriyle birlikte.
/// Durum = <see cref="Id"/> + <see cref="IsDeleted"/>: Id boş → ekle, IsDeleted → sil (değerleriyle), aksi → güncelle.
/// Ürün başına en fazla <see cref="ProductAttributeConsts.MaxAttributesPerProduct"/> (AppService zorlar).
/// </summary>
public class ProductAttributeGraphDto
{
    public Guid Id { get; set; }
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    [Required]
    [StringLength(ProductAttributeConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    /// <summary>Niteliğin değerleri (ör. Renk → Kırmızı/Mavi) — kendi in-memory drill'iyle yönetilir.</summary>
    public List<ProductAttributeValueGraphDto> Values { get; set; } = new();
}

/// <summary>Nitelik DEĞERİ düğümü (ör. "Kırmızı") — attribute grafının çocuğu; aynı Id+IsDeleted diff'i.</summary>
public class ProductAttributeValueGraphDto
{
    public Guid Id { get; set; }
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    [Required]
    [StringLength(ProductAttributeConsts.ValueMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Value { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
}
