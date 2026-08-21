using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// <see cref="RecipeLineQuantityRule"/> saf kural testleri — "ana emtia 0 adet / 0 miktar girilmez"
/// (2026-08-19 Hakan kuralı). Kural iki yerden (giriş guard'ı + satışa hazırlık paneli validator'ı) okunduğundan semantiği
/// burada pinlenir: katalog emtiasında adet ya da miktardan EN AZ BİRİ pozitif; hizmet satırı kapsam dışı.
/// KIRMIZIYSA kural "ikisi birden" ya da "hizmete de uygulanır" biçiminde kaymıştır — testi gevşetme.
/// </summary>
public class RecipeLineQuantityRuleTests
{
    [Fact]
    public void Catalog_commodity_with_zero_quantity_and_zero_amount_is_not_satisfied()
    {
        RecipeLineQuantityRule.IsSatisfied(RecipeComponentType.CatalogCommodity, 0m, 0m).ShouldBeFalse();
    }

    /// <summary>Adetli emtiada miktar adetten türetilir → yalnız adet yeter.</summary>
    [Fact]
    public void Catalog_commodity_with_only_positive_quantity_is_satisfied()
    {
        RecipeLineQuantityRule.IsSatisfied(RecipeComponentType.CatalogCommodity, 2m, 0m).ShouldBeTrue();
    }

    /// <summary>Gramlı emtiada adet boş kalabilir → yalnız miktar yeter.</summary>
    [Fact]
    public void Catalog_commodity_with_only_positive_amount_is_satisfied()
    {
        RecipeLineQuantityRule.IsSatisfied(RecipeComponentType.CatalogCommodity, 0m, 5m).ShouldBeTrue();
    }

    /// <summary>Negatif değer "pozitif" sayılmaz — sıfırla aynı guard'dan döner.</summary>
    [Fact]
    public void Catalog_commodity_with_negative_values_is_not_satisfied()
    {
        RecipeLineQuantityRule.IsSatisfied(RecipeComponentType.CatalogCommodity, -1m, -1m).ShouldBeFalse();
    }

    /// <summary>Hizmetin adedi/miktarı yoktur (bedel türevseldir) → kural hizmeti hiç sorgulamaz.</summary>
    [Fact]
    public void Service_line_is_out_of_scope_and_always_satisfied()
    {
        RecipeLineQuantityRule.RequiresPositiveQuantity(RecipeComponentType.Service).ShouldBeFalse();
        RecipeLineQuantityRule.IsSatisfied(RecipeComponentType.Service, 0m, 0m).ShouldBeTrue();
    }
}
