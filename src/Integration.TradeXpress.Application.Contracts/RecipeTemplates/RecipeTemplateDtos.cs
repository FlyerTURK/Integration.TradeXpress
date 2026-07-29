using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.RecipeTemplates;

/// <summary>Reçete şablonu liste sorgusu (company-owned).</summary>
public class RecipeTemplateListRequestDto : ListRequestDto
{
}

public class RecipeTemplateListDto : EntityDto<Guid>, IListDto<Guid>, IHasIsActive
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Satır sayısı — listede şablonun "dolu mu" olduğu tek bakışta görünsün.</summary>
    public int LineCount { get; set; }

    public override string ToString()
    {
        return Name;
    }
}

public class RecipeTemplateGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    [Required]
    [StringLength(RecipeTemplateConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(RecipeTemplateConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<RecipeTemplateLineDto> Lines { get; set; } = new();
}

public class RecipeTemplateCreateDto : ICreateDto
{
    [Required]
    [StringLength(RecipeTemplateConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    [StringLength(RecipeTemplateConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<RecipeTemplateLineDto> Lines { get; set; } = new();
}

public class RecipeTemplateUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(RecipeTemplateConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(RecipeTemplateConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<RecipeTemplateLineDto> Lines { get; set; } = new();
}

/// <summary>
/// Şablon satırı. <see cref="Id"/> merge anahtarıdır (kategori nitelikleriyle aynı semantik: gelen Id güncellenir,
/// gelmeyen silinir, boş Id yenidir); <see cref="ClientKey"/> yalnız in-memory drill satır kimliğidir.
/// </summary>
public class RecipeTemplateLineDto
{
    public Guid Id { get; set; }

    public Guid ClientKey { get; set; } = Guid.NewGuid();

    public int LineOrder { get; set; }

    /// <summary>Katalog emtiası (yarı mamul) mı, hizmet mi.</summary>
    public RecipeComponentType ComponentType { get; set; } = RecipeComponentType.Service;

    // ── katalog-emtia (yarı mamul) alanları ──
    public ProcessType? CommodityProcessType { get; set; }
    public Guid? CommodityId { get; set; }
    public Guid? CommodityVariantId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public decimal Factor { get; set; }
    public Guid? ValuationUnitId { get; set; }
    public ProcessPaymentType PaymentType { get; set; } = ProcessPaymentType.Normal;
    public decimal PayFactor { get; set; }

    // ── hizmet (türevsel bedel) alanları ──

    /// <summary>Uygulanacak işlem: sabit tutar ekle / yüzde / brütleştir. Taban DAİMA "üstümdeki her şey"dir.</summary>
    public RecipeDerivedOperation? DerivedOperation { get; set; } = RecipeDerivedOperation.Percent;

    /// <summary>Operand — Add'de mutlak tutar, Percent/GrossUp'ta yüzde.</summary>
    public decimal DerivedOperand { get; set; }

    /// <summary>Add operandının para birimi (boşsa ülke birimi). Yalnız Add'de anlamlı.</summary>
    public Guid? PayUnitId { get; set; }

    /// <summary>Yan-maliyet türü (paketleme/kargo/sigorta…) — fiş hizalaması için satıra kopyalanır.</summary>
    public SideCostKind? SideCostKind { get; set; }

    [StringLength(RecipeTemplateConsts.LineDescriptionMaxLength)]
    public string? Description { get; set; }

    public override string ToString()
    {
        return $"{ComponentType}#{LineOrder}";
    }
}

/// <summary>Şablon uygulama sonucu — kullanıcıya kaç varyantın etkilendiği bildirilir.</summary>
public class RecipeTemplateApplyResultDto
{
    public Guid TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public int AffectedVariantCount { get; set; }
    public int AppliedLineCount { get; set; }
}
