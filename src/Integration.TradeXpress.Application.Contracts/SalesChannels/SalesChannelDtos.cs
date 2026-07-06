using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>SalesChannel liste sorgusu (per-tenant). Company-owned: sunucu <see cref="ICurrentCompany"/> ile daraltır
/// (client CompanyId GÖNDERMEZ — Product deseni). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class SalesChannelListRequestDto : ListRequestDto
{
}

/// <summary>Polymorphic liste satırı — TÜM kanal alt-tipleri (base sorgusu). <see cref="ChannelType"/> somut tipi
/// taşır ("Tür" kolonu + düzenlemede doğru forma yönlendirme). Sir alanları (AppSecret/ApiSecret) LİSTEDE YOK.</summary>
public class SalesChannelListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>Kanal türü (N11 / Trendyol) — TPT alt-tipinden türetilir; grid "Tür" kolonu + edit yönlendirmesi.</summary>
    public SalesChannelType ChannelType { get; set; }
}

// ── N11 (SalesChannelTrN11): AppKey/AppSecret ──────────────────────────────────────────────────────

public class SalesChannelTrN11GetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // SIZINTI ÖNLEME: GetDto'da AppKey/AppSecret DAİMA boş döner (AppService redakte eder) → update formunda boş
    // görünür. Kullanıcı doldurursa değişir (application katmanı N11'e doğrular), boş bırakırsa mevcut korunur.
    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string AppKey { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string AppSecret { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

public class SalesChannelTrN11CreateDto : ICreateDto
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Oluşturmada kimlik ZORUNLU (application katmanı N11'e doğrular, geçmezse kayıt açılmaz).
    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    public string AppKey { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    public string AppSecret { get; set; } = string.Empty;
}

public class SalesChannelTrN11UpdateDto : IUpdateDto
{
    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04); benzersizlik AppService'te (TenantId+CompanyId scope).
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Boş = mevcut korunur; doldurulursa (İKİSİ birlikte) application katmanı N11'e doğrular, geçerse günceller.
    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string AppKey { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string AppSecret { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

// ── Trendyol (SalesChannelTrTrendyol): SellerId/ApiKey/ApiSecret ────────────────────────────────────

public class SalesChannelTrTrendyolGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // SellerId matematiksel değil bir KİMLİK → string (sır değil; görünür kalır). UI regex: yalnız rakam.
    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    [RegularExpression("^[0-9]+$", ErrorMessage = "SalesChannel:SellerIdFormat")]
    public string SellerId { get; set; } = string.Empty;

    // SIZINTI ÖNLEME: ApiKey/ApiSecret GetDto'da DAİMA boş döner (redakte). Update'te boş = korunur, dolu = değişir.
    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string ApiKey { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string ApiSecret { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

public class SalesChannelTrTrendyolCreateDto : ICreateDto
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    [RegularExpression("^[0-9]+$", ErrorMessage = "SalesChannel:SellerIdFormat")]
    public string SellerId { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    public string ApiSecret { get; set; } = string.Empty;
}

public class SalesChannelTrTrendyolUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(SalesChannelConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(SalesChannelConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // SellerId görünür kimlik → daima gönderilir/güncellenir (sır değil).
    [Required]
    [StringLength(SalesChannelConsts.ConfigMaxLength, MinimumLength = 1)]
    [RegularExpression("^[0-9]+$", ErrorMessage = "SalesChannel:SellerIdFormat")]
    public string SellerId { get; set; } = string.Empty;

    // Boş = mevcut korunur; doldurulursa (İKİSİ birlikte) güncellenir (Trendyol test API'si yok → doğrulama yapılmaz).
    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string ApiKey { get; set; } = string.Empty;

    [StringLength(SalesChannelConsts.ConfigMaxLength)]
    public string ApiSecret { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
