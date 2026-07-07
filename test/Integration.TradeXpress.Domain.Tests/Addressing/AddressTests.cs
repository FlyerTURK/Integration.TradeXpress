using Integration.Framework;
using Integration.Framework.Addressing;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Addressing;

/// <summary>
/// Framework <see cref="Address"/> value object birim testleri (§8: reusable framework kodu test ister).
/// Değer eşitliği + zorunlu alan validasyonu + varsayılan ülke + opsiyonel alan null'lama.
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
}
