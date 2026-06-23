using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Countries;

/// <summary>Ülke listesi sorgusu (host kataloğu + tenant'ın kendileri).</summary>
public class CountryListRequestDto : ListRequestDto
{
}

public class CountryListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DefaultCurrencyCode { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsGlobal { get; set; }
}

public class CountryGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    // Client validasyonu modelin ÜZERİNDE (agnostic Form, LocalizedDataAnnotationsValidator ile doğrular).
    // Server-input doğrulaması Create/Update DTO'larında kalır. Create/Update ile aynı kurallar.
    [Required]
    [StringLength(CountryConsts.CodeMaxLength, MinimumLength = 2)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(CountryConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string? DefaultCurrencyCode { get; set; }

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsGlobal { get; set; }
}

public class CountryCreateDto : ICreateDto
{
    [Required]
    [StringLength(CountryConsts.CodeMaxLength, MinimumLength = 2)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(CountryConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string DefaultCurrencyCode { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
}

public class CountryUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(CountryConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string DefaultCurrencyCode { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}
