using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Values;

namespace Integration.Framework.Addressing;

/// <summary>
/// Yeniden-kullanılabilir posta adresi (ABP <see cref="ValueObject"/>) — Fatura/Şirket/Şube/ContactPerson vb.
/// gömülür (EF <c>OwnsOne</c>/<c>OwnsMany</c>). İsim-bazlı (insan-okur) + opsiyonel yapısal kodlar
/// (<see cref="CityCode"/>/<see cref="DistrictCode"/>; N11/plaka gibi kodlu sistemler için — push'ta lookup gerekmez).
/// Immutable, değer eşitliği. Zorunlu: <see cref="City"/> + <see cref="Line"/>. Ülke varsayılan "TR".
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
        string? districtCode = null)
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

    #endregion

    #region Methods

    public override string ToString()
    {
        var parts = new[] { Line, Neighborhood, District, City, PostalCode }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(", ", parts);
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
    }

    #endregion
}
