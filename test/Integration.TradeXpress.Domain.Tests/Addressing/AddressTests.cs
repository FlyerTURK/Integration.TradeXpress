using System;
using Integration.Framework;
using Integration.Framework.Addressing;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Addressing;

/// <summary>
/// Framework <see cref="Address"/> value object birim testleri (§8: reusable framework kodu test ister).
/// Değer eşitliği + zorunlu alan validasyonu + varsayılan ülke + opsiyonel alan null'lama + coğrafya referansları + UBL projeksiyonu.
/// </summary>
public class AddressTests
{
    [Fact]
    public void Equal_addresses_are_value_equal()
    {
        var a = new Address("İstanbul", "Bağdat Cad. No:1", district: "Kadıköy", postalCode: "34710");
        var b = new Address("İstanbul", "Bağdat Cad. No:1", district: "Kadıköy", postalCode: "34710");

        a.ShouldBe(b);
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Different_addresses_are_not_equal()
    {
        var a = new Address("İstanbul", "Cad. 1");
        var b = new Address("Ankara", "Cad. 1");

        a.ShouldNotBe(b);
    }

    [Fact]
    public void City_and_Line_are_required()
    {
        Should.Throw<RequiredPropertyException>(() => new Address("", "Cad. 1"));
        Should.Throw<RequiredPropertyException>(() => new Address("İstanbul", "   "));
    }

    [Fact]
    public void CountryCode_defaults_to_TR_and_is_uppercased()
    {
        new Address("İstanbul", "Cad. 1").CountryCode.ShouldBe("TR");
        new Address("İstanbul", "Cad. 1", countryCode: "de").CountryCode.ShouldBe("DE");
    }

    [Fact]
    public void Blank_optional_fields_become_null()
    {
        var a = new Address("İstanbul", "Cad. 1", district: "   ", postalCode: null, cityCode: "34");

        a.District.ShouldBeNull();
        a.PostalCode.ShouldBeNull();
        a.CityCode.ShouldBe("34");
    }

    [Fact]
    public void Geography_refs_default_to_null_when_not_supplied()
    {
        // Additive ctor: eski çağrı yerleri (yeni param'ları geçmeyen) null coğrafya referanslarıyla kurulur.
        var a = new Address("İstanbul", "Cad. 1");

        a.AdministrativeAreaId.ShouldBeNull();
        a.LocalityId.ShouldBeNull();
        a.AdministrativeAreaIsoCode.ShouldBeNull();
    }

    [Fact]
    public void Geography_refs_are_captured_and_iso_code_trimmed()
    {
        var areaId = Guid.NewGuid();
        var localityId = Guid.NewGuid();
        var a = new Address(
            "İstanbul", "Cad. 1",
            administrativeAreaId: areaId,
            localityId: localityId,
            administrativeAreaIsoCode: "  TR-34  ");

        a.AdministrativeAreaId.ShouldBe(areaId);
        a.LocalityId.ShouldBe(localityId);
        a.AdministrativeAreaIsoCode.ShouldBe("TR-34");
    }

    [Fact]
    public void Blank_iso_code_becomes_null()
    {
        new Address("İstanbul", "Cad. 1", administrativeAreaIsoCode: "   ").AdministrativeAreaIsoCode.ShouldBeNull();
    }

    [Fact]
    public void Geography_refs_participate_in_value_equality()
    {
        var areaId = Guid.NewGuid();
        var a = new Address("İstanbul", "Cad. 1", administrativeAreaId: areaId);
        var b = new Address("İstanbul", "Cad. 1", administrativeAreaId: areaId);
        var c = new Address("İstanbul", "Cad. 1", administrativeAreaId: Guid.NewGuid());

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldNotBe(c);
    }

    [Fact]
    public void ToUblPostalAddress_maps_roles_per_UBL_mapping()
    {
        var a = new Address(
            city: "İstanbul",
            line: "Bağdat Cad. No:1",
            district: "Kadıköy",
            neighborhood: "Caddebostan",
            postalCode: "34710",
            countryCode: "TR",
            administrativeAreaIsoCode: "TR-34");

        var ubl = a.ToUblPostalAddress();

        ubl.StreetName.ShouldBe("Bağdat Cad. No:1");        // Line → StreetName
        ubl.CitySubdivisionName.ShouldBe("Caddebostan");    // Neighborhood → CitySubdivisionName
        ubl.CityName.ShouldBe("Kadıköy");                   // District → CityName
        ubl.PostalZone.ShouldBe("34710");                   // PostalCode → PostalZone
        ubl.Region.ShouldBe("İstanbul");                    // City → CountrySubentity (Region)
        ubl.CountrySubentityCode.ShouldBe("TR-34");         // AdministrativeAreaIsoCode → CountrySubentityCode
        ubl.CountryIdentificationCode.ShouldBe("TR");       // CountryCode → Country/IdentificationCode
    }
}
