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
    /// <summary>Görüntü alanı — <see cref="DefaultCurrencyUnitId"/>'den çözülen birim kodu (grid kolonu).</summary>
    public string? DefaultCurrencyCode { get; set; }
    /// <summary>Varsayılan para birimi — CurrencyUnit'e id-only referans (otoriter alan).</summary>
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

    /// <summary>Varsayılan para birimi — CurrencyUnit'e id-only referans (otoriter alan; combo buna bağlanır).</summary>
    [Required]
    public Guid? DefaultCurrencyUnitId { get; set; }

    /// <summary>Görüntü alanı — id'den çözülen birim kodu (server doldurur; form bind ETMEZ).</summary>
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
    public Guid? DefaultCurrencyUnitId { get; set; }

    public int DisplayOrder { get; set; }
}

public class CountryUpdateDto : CatalogUpdateDtoBase
{
    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04: host CurrencyUnit kayıtları dışında tüm kodlar değiştirilebilir).
    [Required]
    [StringLength(CountryConsts.CodeMaxLength, MinimumLength = 2)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(CountryConsts.NameMaxLength)]
    public override string Name { get; set; } = string.Empty;

    [Required]
    public Guid? DefaultCurrencyUnitId { get; set; }

    public int DisplayOrder { get; set; }
}
