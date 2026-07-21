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

    /// <summary>Adres modeli il/eyalet (ISO 3166-2) seviyesi kullanır mı — coğrafya picker'ı bu bayrağa göre
    /// il/eyalet combo'sunu gösterir/gizler (false → tek sembolik ana alan, doğrudan ilçe).</summary>
    public bool UsesAdministrativeArea { get; set; } = true;

    /// <summary>Adres modeli alt-yerellik (mahalle) seviyesi kullanır mı (yalnız TR gibi).</summary>
    public bool UsesSubLocality { get; set; }

    /// <summary>İdari-alan etiketi tipi (libaddressinput) — picker il/eyalet başlığını buna göre uyarlar (TR→İl, US→Eyalet).</summary>
    public AdministrativeAreaType AdministrativeAreaType { get; set; }

    /// <summary>Yerellik etiketi tipi — picker ilçe/şehir başlığını buna göre uyarlar (TR→İlçe, US→Şehir).</summary>
    public LocalityType LocalityType { get; set; }

    /// <summary>Alt-yerellik etiketi tipi — picker mahalle başlığını buna göre uyarlar (TR→Mahalle).</summary>
    public SubLocalityType SubLocalityType { get; set; }

    /// <summary>Posta kodu etiketi tipi — posta kodu alanı başlığını buna göre uyarlar (US→ZIP, IN→PIN, IE→Eircode).</summary>
    public PostalCodeType PostalCodeType { get; set; }
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

    /// <summary>Adres-format metadatası (libaddressinput etiket tipleri) — düzenleme henüz seed yönetimli (Create/Update DTO'ya EKLENMEDİ), salt görüntü.</summary>
    public AdministrativeAreaType AdministrativeAreaType { get; set; }
    public LocalityType LocalityType { get; set; }
    public SubLocalityType SubLocalityType { get; set; }
    public PostalCodeType PostalCodeType { get; set; }
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
