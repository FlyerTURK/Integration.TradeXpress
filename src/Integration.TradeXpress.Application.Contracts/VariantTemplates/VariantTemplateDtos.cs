using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Variants;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.VariantTemplates;

/// <summary>VariantTemplate liste sorgusu (per-tenant, company-owned). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class VariantTemplateListRequestDto : ListRequestDto
{
}

public class VariantTemplateListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}

public class VariantTemplateGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    [Required]
    [StringLength(VariantTemplateConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(VariantTemplateConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(VariantTemplateConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>Şablonun özellik grupları + değerleri (in-memory drill; grup → değer iç içe).</summary>
    public List<VariantTemplateAttributeDto> Attributes { get; set; } = new();
}

public class VariantTemplateCreateDto : ICreateDto
{
    [Required]
    [StringLength(VariantTemplateConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(VariantTemplateConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    [StringLength(VariantTemplateConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<VariantTemplateAttributeDto> Attributes { get; set; } = new();
}

public class VariantTemplateUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(VariantTemplateConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(VariantTemplateConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(VariantTemplateConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<VariantTemplateAttributeDto> Attributes { get; set; } = new();
}

/// <summary>Şablon özellik grubu (ör. "Renk") + değerleri. <see cref="ClientKey"/> yalnız in-memory DrillList
/// satır kimliği (persist edilmez). Ad zorunlu (boş grup elenir).</summary>
public class VariantTemplateAttributeDto
{
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(EntityVariantConsts.AttributeNameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public List<VariantTemplateAttributeValueDto> Values { get; set; } = new();
}

/// <summary>Şablon özellik değeri (ör. "Kırmızı", "XL"). <see cref="ClientKey"/> yalnız in-memory drill satır kimliği.</summary>
public class VariantTemplateAttributeValueDto
{
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(EntityVariantConsts.AttributeValueMaxLength, MinimumLength = 1)]
    public string Value { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
}
