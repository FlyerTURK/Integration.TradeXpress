namespace Integration.Framework.Addressing;

/// <summary>
/// Yeniden-kullanılabilir adres EDİT modeli — <see cref="Address"/> VO alanlarının düzenlenebilir (get/set)
/// ikizi. Herhangi bir flat adres DTO'su (ör. <c>BranchAddressDto</c>) bunu implement eder; ortak
/// <c>AddressFields</c> bileşeni buna bind eder → adres formu tek yerde, tüm entity'lerde aynı (DRY).
///
/// <para>Alan rolleri VO ile birebir (zorunlu: <see cref="City"/> + <see cref="Line"/>; ülke varsayılan "TR").
/// Coğrafya picker'ı <see cref="City"/>/<see cref="District"/>/<see cref="Neighborhood"/> + kodları +
/// id-only köprüleri (<see cref="AdministrativeAreaId"/>/<see cref="LocalityId"/>/<see cref="AdministrativeAreaIsoCode"/>)
/// + <see cref="CountryCode"/> DOLDURUR; serbest-metin yalnız <see cref="Line"/>/<see cref="PostalCode"/>/<see cref="Title"/>.</para>
/// </summary>
public interface IAddressEditModel
{
    /// <summary>Adres etiketi ("Fatura Adresi", "Depo") — opsiyonel.</summary>
    string? Title { get; set; }

    /// <summary>İl adı (zorunlu; picker doldurur).</summary>
    string City { get; set; }

    /// <summary>Açık adres — cadde/sokak/no (zorunlu; serbest-metin).</summary>
    string Line { get; set; }

    /// <summary>İlçe adı (opsiyonel; picker doldurur).</summary>
    string? District { get; set; }

    /// <summary>Mahalle adı (opsiyonel; picker doldurur).</summary>
    string? Neighborhood { get; set; }

    /// <summary>Posta kodu (opsiyonel; serbest-metin).</summary>
    string? PostalCode { get; set; }

    /// <summary>Ülke kodu (ISO-3166 alpha-2, varsayılan "TR"; picker doldurur).</summary>
    string CountryCode { get; set; }

    /// <summary>Ülke ADI — SALT GÖRÜNTÜ (ör. "Türkiye"). Otoriter alan <see cref="CountryCode"/>'dur; bu yalnız
    /// adres özetinde kod yerine okunabilir ad göstermek içindir. Picker seçim yaparken doldurur; sunucu DTO
    /// kurarken katalogdan çözer. Boşsa özet koda düşer (<c>AddressDisplay</c>) → hiçbir yüzey kırılmaz.
    /// <para>Denormalize görüntü alanı deseni — <c>FollowingUnitName</c>/<c>CompanyCode</c>/<c>BaseCurrencyCode</c>
    /// ile aynı: istemci formatter'ı aptal ve senkron kalsın diye ad SUNUCUDA çözülür, UI'da arama yapılmaz.</para></summary>
    string? CountryName { get; set; }

    /// <summary>Opsiyonel yapısal il kodu (plaka / kanal il kodu; picker doldurur).</summary>
    string? CityCode { get; set; }

    /// <summary>Opsiyonel yapısal ilçe kodu (picker doldurur).</summary>
    string? DistrictCode { get; set; }

    /// <summary>Opsiyonel çekirdek coğrafya idari-alan (il/eyalet) id'si — id-only köprü (picker doldurur).</summary>
    Guid? AdministrativeAreaId { get; set; }

    /// <summary>Opsiyonel çekirdek coğrafya yerellik (ilçe) id'si — id-only köprü (picker doldurur).</summary>
    Guid? LocalityId { get; set; }

    /// <summary>Opsiyonel ISO 3166-2 idari-alan kodu (ör. "TR-34") — UBL projeksiyonu için (picker doldurur).</summary>
    string? AdministrativeAreaIsoCode { get; set; }

    /// <summary>Opsiyonel bina adı — UBL <c>BuildingName</c>.</summary>
    string? BuildingName { get; set; }

    /// <summary>Opsiyonel bina numarası — UBL <c>BuildingNumber</c>.</summary>
    string? BuildingNumber { get; set; }

    /// <summary>Opsiyonel oda/daire — UBL <c>Room</c>.</summary>
    string? Room { get; set; }

    /// <summary>Opsiyonel kat — UBL <c>Floor</c>.</summary>
    string? Floor { get; set; }

    /// <summary>Opsiyonel posta kutusu — UBL <c>Postbox</c>.</summary>
    string? Postbox { get; set; }

    /// <summary>Opsiyonel ek cadde/sokak adı — UBL <c>AdditionalStreetName</c>.</summary>
    string? AdditionalStreetName { get; set; }
}
