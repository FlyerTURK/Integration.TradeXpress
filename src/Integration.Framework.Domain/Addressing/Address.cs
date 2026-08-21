using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Values;

namespace Integration.Framework.Addressing;

/// <summary>
/// Yeniden-kullanılabilir posta adresi (ABP <see cref="ValueObject"/>) — Fatura/Şirket/Şube/ContactPerson vb.
/// gömülür (EF <c>OwnsOne</c>/<c>OwnsMany</c>). İsim-bazlı (insan-okur) + opsiyonel yapısal kodlar
/// (<see cref="CityCode"/>/<see cref="DistrictCode"/>; N11/plaka gibi kodlu sistemler için — push'ta lookup gerekmez).
/// Immutable, değer eşitliği. Zorunlu: <see cref="City"/> + <see cref="Line"/>. Ülke varsayılan "TR".
///
/// <para><b>Coğrafya referansları (opsiyonel, additive):</b> <see cref="AdministrativeAreaId"/> /
/// <see cref="LocalityId"/> core coğrafya kataloğuna (host-global) id-only kolondur (nav/FK YOK; picker doldurur),
/// <see cref="AdministrativeAreaIsoCode"/> ISO 3166-2 kodudur (ör. "TR-34"). Kodlu push sistemleri (N11) bunları
/// KULLANMAZ — mevcut <see cref="City"/>/<see cref="District"/> + <see cref="CityCode"/>/<see cref="DistrictCode"/>
/// okumaya devam eder; yeni alanlar yalnız zenginleştirme (fatura/UBL) içindir.</para>
///
/// <para><b>UBL <c>PostalAddress</c> rol eşlemesi</b> (onaylı, 2026-07-21; bkz. <see cref="ToUblPostalAddress"/>):
/// <see cref="City"/> (İl) → <c>CityName</c> · <see cref="District"/> (İlçe) → <c>CitySubdivisionName</c> ·
/// <see cref="Neighborhood"/> (Mahalle) → <c>District</c> · <see cref="Line"/> (Cadde/Sokak) → <c>StreetName</c> ·
/// <see cref="AdditionalStreetName"/> → <c>AdditionalStreetName</c> · <see cref="BuildingName"/> → <c>BuildingName</c> ·
/// <see cref="BuildingNumber"/> → <c>BuildingNumber</c> · <see cref="Room"/> → <c>Room</c> · <see cref="Floor"/> → <c>Floor</c> ·
/// <see cref="Postbox"/> → <c>Postbox</c> · <see cref="PostalCode"/> → <c>PostalZone</c> ·
/// <see cref="AdministrativeAreaIsoCode"/> → <c>CountrySubentityCode</c> ·
/// <see cref="CountryCode"/> → <c>Country/IdentificationCode</c> (ISO 3166-1).</para>
/// </summary>
public class Address : ValueObject
{
    #region Constructors

    private Address()
    {
    }

    public Address(
        string city,
        string line,
        string? district = null,
        string? neighborhood = null,
        string? postalCode = null,
        string? countryCode = "TR",
        string? title = null,
        string? cityCode = null,
        string? districtCode = null,
        Guid? administrativeAreaId = null,
        Guid? localityId = null,
        string? administrativeAreaIsoCode = null,
        string? buildingName = null,
        string? buildingNumber = null,
        string? room = null,
        string? floor = null,
        string? postbox = null,
        string? additionalStreetName = null)
    {
        City = StringFieldGuard.EnsureRequiredText(city, nameof(City), 1, AddressConsts.CityMaxLength);
        Line = StringFieldGuard.EnsureRequiredText(line, nameof(Line), 1, AddressConsts.LineMaxLength);
        CountryCode = StringFieldGuard.EnsureRequiredText(
            string.IsNullOrWhiteSpace(countryCode) ? "TR" : countryCode.Trim().ToUpperInvariant(),
            nameof(CountryCode), 2, AddressConsts.CountryCodeMaxLength);
        District = StringFieldGuard.EnsureOptionalText(district, nameof(District), 1, AddressConsts.DistrictMaxLength);
        Neighborhood = StringFieldGuard.EnsureOptionalText(neighborhood, nameof(Neighborhood), 1, AddressConsts.NeighborhoodMaxLength);
        PostalCode = StringFieldGuard.EnsureOptionalText(postalCode, nameof(PostalCode), 1, AddressConsts.PostalCodeMaxLength);
        Title = StringFieldGuard.EnsureOptionalText(title, nameof(Title), 1, AddressConsts.TitleMaxLength);
        CityCode = StringFieldGuard.EnsureOptionalText(cityCode, nameof(CityCode), 1, AddressConsts.CodeMaxLength);
        DistrictCode = StringFieldGuard.EnsureOptionalText(districtCode, nameof(DistrictCode), 1, AddressConsts.CodeMaxLength);
        AdministrativeAreaId = administrativeAreaId;
        LocalityId = localityId;
        AdministrativeAreaIsoCode = StringFieldGuard.EnsureOptionalText(
            administrativeAreaIsoCode, nameof(AdministrativeAreaIsoCode), 1, AddressConsts.IsoSubentityCodeMaxLength);
        BuildingName = StringFieldGuard.EnsureOptionalText(buildingName, nameof(BuildingName), 1, AddressConsts.BuildingNameMaxLength);
        BuildingNumber = StringFieldGuard.EnsureOptionalText(buildingNumber, nameof(BuildingNumber), 1, AddressConsts.BuildingNumberMaxLength);
        Room = StringFieldGuard.EnsureOptionalText(room, nameof(Room), 1, AddressConsts.RoomMaxLength);
        Floor = StringFieldGuard.EnsureOptionalText(floor, nameof(Floor), 1, AddressConsts.FloorMaxLength);
        Postbox = StringFieldGuard.EnsureOptionalText(postbox, nameof(Postbox), 1, AddressConsts.PostboxMaxLength);
        AdditionalStreetName = StringFieldGuard.EnsureOptionalText(additionalStreetName, nameof(AdditionalStreetName), 1, AddressConsts.AdditionalStreetNameMaxLength);
    }

    #endregion

    #region Properties

    /// <summary>İl adı (zorunlu).</summary>
    public string City { get; } = string.Empty;

    /// <summary>Açık adres — cadde/sokak/no (zorunlu).</summary>
    public string Line { get; } = string.Empty;

    /// <summary>Ülke kodu (ISO-3166 alpha-2, varsayılan "TR").</summary>
    public string CountryCode { get; } = "TR";

    public string? District { get; }
    public string? Neighborhood { get; }
    public string? PostalCode { get; }

    /// <summary>Adres etiketi ("Fatura Adresi", "Depo").</summary>
    public string? Title { get; }

    /// <summary>Opsiyonel yapısal il kodu (plaka / N11 il kodu) — kodlu sistem eşlemesi için.</summary>
    public string? CityCode { get; }

    /// <summary>Opsiyonel yapısal ilçe kodu (ilçe id / N11).</summary>
    public string? DistrictCode { get; }

    /// <summary>Opsiyonel core coğrafya idari-alan (il/eyalet) id'si — id-only kolon (nav/FK YOK). Picker doldurur;
    /// kodlu push sistemleri kullanmaz.</summary>
    public Guid? AdministrativeAreaId { get; }

    /// <summary>Opsiyonel core coğrafya yerellik (ilçe) id'si — id-only kolon (nav/FK YOK).</summary>
    public Guid? LocalityId { get; }

    /// <summary>Opsiyonel ISO 3166-2 idari-alan kodu (ör. "TR-34") — UBL <c>CountrySubentityCode</c>.</summary>
    public string? AdministrativeAreaIsoCode { get; }

    /// <summary>Opsiyonel bina adı — UBL <c>BuildingName</c>.</summary>
    public string? BuildingName { get; }

    /// <summary>Opsiyonel bina numarası — UBL <c>BuildingNumber</c>.</summary>
    public string? BuildingNumber { get; }

    /// <summary>Opsiyonel oda/daire — UBL <c>Room</c>.</summary>
    public string? Room { get; }

    /// <summary>Opsiyonel kat — UBL <c>Floor</c>.</summary>
    public string? Floor { get; }

    /// <summary>Opsiyonel posta kutusu — UBL <c>Postbox</c>.</summary>
    public string? Postbox { get; }

    /// <summary>Opsiyonel ek cadde/sokak adı — UBL <c>AdditionalStreetName</c>.</summary>
    public string? AdditionalStreetName { get; }

    #endregion

    #region Methods

    public override string ToString()
    {
        var parts = new[] { Line, Neighborhood, District, City, PostalCode }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(", ", parts);
    }

    /// <summary>Adresi UBL <c>PostalAddress</c> projeksiyonuna çevirir (fatura/e-fatura dilimine hazır; rol eşlemesi
    /// tip özetindedir). Salt okuma — yeni durum tutmaz; alanları UBL rollerine yeniden adlandırarak taşır.</summary>
    public UblPostalAddress ToUblPostalAddress()
    {
        return new UblPostalAddress(
            StreetName: Line,
            AdditionalStreetName: AdditionalStreetName,
            BuildingName: BuildingName,
            BuildingNumber: BuildingNumber,
            Room: Room,
            Floor: Floor,
            Postbox: Postbox,
            CitySubdivisionName: District,       // İlçe → CitySubdivisionName
            CityName: City,                      // İl → CityName
            PostalZone: PostalCode,
            District: Neighborhood,              // Mahalle → District
            CountrySubentityCode: AdministrativeAreaIsoCode,
            CountryIdentificationCode: CountryCode);
    }

    // Değer eşitliği/hashcode/== Framework ValueObject tabanında (GetAtomicValues üzerinden) — burada yalnız alanlar.
    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return City;
        yield return Line;
        yield return CountryCode;
        yield return District;
        yield return Neighborhood;
        yield return PostalCode;
        yield return Title;
        yield return CityCode;
        yield return DistrictCode;
        yield return AdministrativeAreaId;
        yield return LocalityId;
        yield return AdministrativeAreaIsoCode;
        yield return BuildingName;
        yield return BuildingNumber;
        yield return Room;
        yield return Floor;
        yield return Postbox;
        yield return AdditionalStreetName;
    }

    #endregion
}
