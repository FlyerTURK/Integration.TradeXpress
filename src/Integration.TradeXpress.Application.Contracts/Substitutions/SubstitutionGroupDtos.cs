using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Substitutions;

/// <summary>Muadil grubu liste sorgusu (per-tenant + company-owned). Client CompanyId GÖNDERMEZ —
/// sunucu ICompanyOwned global query-filter'ı ile çalışılan şirkete scope'lar (grid standardı).</summary>
public class SubstitutionGroupListRequestDto : ListRequestDto
{
}

public class SubstitutionGroupListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SubstitutionType Type { get; set; }
    public ToleranceType ToleranceType { get; set; }
    public decimal ToleranceValue { get; set; }
    public bool IsActive { get; set; }
}

public class SubstitutionGroupGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(SubstitutionGroupConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SubstitutionGroupConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Muadil türü — şimdilik yalnız Metal (UI'da sabit/disabled).</summary>
    public SubstitutionType Type { get; set; } = SubstitutionType.Metal;

    public ToleranceType ToleranceType { get; set; } = ToleranceType.Amount;

    /// <summary>Tolerans değeri — 0 = mutlak eşitlik; negatif olamaz (entity fail-fast).</summary>
    public decimal ToleranceValue { get; set; }

    [StringLength(SubstitutionGroupConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Sıralı emtia satırları (graf düğümleri; Id + IsDeleted ile diff — Account/SubAccount deseni).
    /// Liste sırası (DisplayOrder) = kullanıcı-kontrollü TÜKETİM ÖNCELİĞİ.</summary>
    public List<SubstitutionGroupItemGraphDto> Items { get; set; } = new();
}

public class SubstitutionGroupCreateDto : ICreateDto
{
    [Required]
    [StringLength(SubstitutionGroupConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SubstitutionGroupConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    public SubstitutionType Type { get; set; } = SubstitutionType.Metal;

    public ToleranceType ToleranceType { get; set; } = ToleranceType.Amount;

    public decimal ToleranceValue { get; set; }

    [StringLength(SubstitutionGroupConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<SubstitutionGroupItemGraphDto> Items { get; set; } = new();
}

public class SubstitutionGroupUpdateDto : IUpdateDto
{
    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04: host CurrencyUnit kayıtları dışında tüm kodlar değiştirilebilir).
    [Required]
    [StringLength(SubstitutionGroupConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SubstitutionGroupConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    public SubstitutionType Type { get; set; } = SubstitutionType.Metal;

    public ToleranceType ToleranceType { get; set; } = ToleranceType.Amount;

    public decimal ToleranceValue { get; set; }

    [StringLength(SubstitutionGroupConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public List<SubstitutionGroupItemGraphDto> Items { get; set; } = new();
}

/// <summary>
/// Muadil grubunun emtia satırı DÜĞÜMÜ — grup edit'inde in-memory drill + grup save'i içindir
/// (SubAccountGraphDto deseni). Durum = <see cref="Id"/> + <see cref="IsDeleted"/>: Id boş → ekle,
/// IsDeleted → sil, aksi → güncelle. <see cref="MetalCode"/> yalnız gösterimdir (sunucu doldurur,
/// client seçim değişince günceller); yazmada esas alan <see cref="MetalId"/>'dir.
/// </summary>
public class SubstitutionGroupItemGraphDto
{
    public Guid Id { get; set; }
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    [Required]
    public Guid? MetalId { get; set; }

    /// <summary>Gösterim amaçlı maden kodu (drill grid kolonu) — persist edilmez.</summary>
    public string MetalCode { get; set; } = string.Empty;

    /// <summary>Tüketim önceliği — küçük önce tüketilir (liste sırası kullanıcı-kontrollü).</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Muadile DAHİL varyantlar (OPT-IN). <b>Boş = yalnız ANA varyant</b> (statüko; yeni doğan varyant
    /// otomatik dahil DEĞİL). "{yalnız ana}" seçimi yazma sınırında boş listeye normalize edilir (tek temsil).
    /// Grup grafıyla birlikte kaydedilir — ayrı servis çağrısı yok (Varyant Kapsamı ağacı in-memory düzenler).</summary>
    public List<Guid> IncludedVariantIds { get; set; } = new();
}
