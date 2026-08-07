using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;

namespace Integration.TradeXpress.SpecialCodes;

public class SpecialCodeListRequestDto : ListRequestDto
{
    /// <summary>Çalışılan şirket — görünür kayıtlar host/holding-host(null) + bu şirkete-özel olanlar.</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>Bağlam süzgeci — hedef entity tipi adı (boş = tümü).</summary>
    public string? EntityName { get; set; }

    /// <summary>Bağlam süzgeci — hedef property adı (boş = tümü).</summary>
    public string? PropertyName { get; set; }
}

public class SpecialCodeListDto : CatalogListDtoBase
{
    public string EntityName { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public Guid? CompanyId { get; set; }
}

public class SpecialCodeGetDto : CatalogGetDtoBase, IHasCode
{
    [Required]
    [StringLength(SpecialCodeConsts.CodeMaxLength, MinimumLength = SpecialCodeConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SpecialCodeConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    /// <summary>Bağlam — set-once (create'te dolar, update'te değişmez). Picker ön-doldurur.</summary>
    [StringLength(SpecialCodeConsts.EntityNameMaxLength)] public string EntityName { get; set; } = string.Empty;
    [StringLength(SpecialCodeConsts.PropertyNameMaxLength)] public string PropertyName { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    [StringLength(SpecialCodeConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public Guid? CompanyId { get; set; }
}

public class SpecialCodeCreateDto : CatalogCreateDtoBase
{
    /// <summary>Sahip şirket — client çalışılan şirketi atar (null = tenant-geneli paylaşım).</summary>
    public Guid? CompanyId { get; set; }

    [Required]
    [StringLength(SpecialCodeConsts.CodeMaxLength, MinimumLength = SpecialCodeConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SpecialCodeConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    /// <summary>Bağlam — hangi (entity, property) için kod. Picker'dan gelir (zorunlu).</summary>
    [Required][StringLength(SpecialCodeConsts.EntityNameMaxLength)] public string EntityName { get; set; } = string.Empty;
    [Required][StringLength(SpecialCodeConsts.PropertyNameMaxLength)] public string PropertyName { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    [StringLength(SpecialCodeConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class SpecialCodeUpdateDto : CatalogUpdateDtoBase
{
    // Kod DÜZENLENEBİLİR; bağlam (EntityName/PropertyName) set-once → update'te YOK (değiştirilemez).
    [Required]
    [StringLength(SpecialCodeConsts.CodeMaxLength, MinimumLength = SpecialCodeConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SpecialCodeConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    [StringLength(SpecialCodeConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}
