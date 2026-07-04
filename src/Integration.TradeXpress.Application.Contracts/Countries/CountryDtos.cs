using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;

namespace Integration.TradeXpress.Countries;

/// <summary>Ülke listesi sorgusu (host kataloğu + tenant'ın kendileri).</summary>
public class CountryListRequestDto : ListRequestDto
{
}

public class CountryListDto : CatalogListDtoBase
{
    public string? DefaultCurrencyCode { get; set; }
    /// <summary>DefaultCurrencyCode'a karşılık gelen CurrencyUnit.Id (link için; Code'dan çözülür, FK değil).</summary>
    public Guid? DefaultCurrencyUnitId { get; set; }
    public int DisplayOrder { get; set; }
}

// Not: CountryGetDto bilinçli olarak IHasCode DEĞİL (başlıkta kod gösterilmez) — base bunu dayatmaz.
public class CountryGetDto : CatalogGetDtoBase
{
    // Client validasyonu modelin ÜZERİNDE (agnostic Form, LocalizedDataAnnotationsValidator ile doğrular).
    // Server-input doğrulaması Create/Update DTO'larında kalır. Create/Update ile aynı kurallar.
    [Required]
    [StringLength(CountryConsts.CodeMaxLength, MinimumLength = 2)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(CountryConsts.NameMaxLength)]
    public override string Name { get; set; } = string.Empty;

    [Required]
    public string? DefaultCurrencyCode { get; set; }

    public int DisplayOrder { get; set; }
}

public class CountryCreateDto : CatalogCreateDtoBase
{
    [Required]
    [StringLength(CountryConsts.CodeMaxLength, MinimumLength = 2)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(CountryConsts.NameMaxLength)]
    public override string Name { get; set; } = string.Empty;

    [Required]
    public string DefaultCurrencyCode { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
}

public class CountryUpdateDto : CatalogUpdateDtoBase
{
    [Required]
    [StringLength(CountryConsts.NameMaxLength)]
    public override string Name { get; set; } = string.Empty;

    [Required]
    public string DefaultCurrencyCode { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }
}
