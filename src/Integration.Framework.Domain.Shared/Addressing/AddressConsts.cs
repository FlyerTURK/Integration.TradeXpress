namespace Integration.Framework.Addressing;

/// <summary>Yeniden-kullanılabilir <see cref="Address"/> value object alan sınırları (Framework — her projede ortak).</summary>
public static class AddressConsts
{
    public const int TitleMaxLength = 64;
    public const int CountryCodeMaxLength = 2;    // ISO-3166 alpha-2
    public const int CityMaxLength = 64;
    public const int DistrictMaxLength = 64;
    public const int NeighborhoodMaxLength = 128;
    public const int LineMaxLength = 512;
    public const int PostalCodeMaxLength = 16;

    // UBL PostalAddress zenginleştirme alanları (opsiyonel) — bina/oda/kat/posta-kutusu + ek cadde adı.
    public const int BuildingNameMaxLength = 128;
    public const int BuildingNumberMaxLength = 32;
    public const int RoomMaxLength = 32;
    public const int FloorMaxLength = 16;
    public const int PostboxMaxLength = 32;
    public const int AdditionalStreetNameMaxLength = 256;

    /// <summary>Opsiyonel yapısal kod (CityCode/DistrictCode — plaka / N11 gibi kodlu sistemler).</summary>
    public const int CodeMaxLength = 16;

    /// <summary>Opsiyonel ISO 3166-2 idari-alan kodu (<c>AdministrativeAreaIsoCode</c>, ör. "TR-34", "US-AL") — UBL
    /// <c>CountrySubentityCode</c>'a projekte edilir. Core coğrafyanın kod uzunluğuyla (Iso3166_2CodeMaxLength) hizalı.</summary>
    public const int IsoSubentityCodeMaxLength = 16;
}
