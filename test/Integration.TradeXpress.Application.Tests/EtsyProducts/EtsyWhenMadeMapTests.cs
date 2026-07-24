using Integration.TradeXpress.Products;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary><see cref="EtsyProductClient.MapWhenMade"/> birim testleri — Etsy <c>when_made</c> wire string'i →
/// <see cref="ProductMadePeriod"/> (K9). 19 openapi değerinin TAMAMI pinli (kronolojik numaralamayla birlikte);
/// alan gelmediyse (null/boş) null (ürün varsayılanı korunur); BİLİNMEYEN değer sessizce yutulMAZ → fail-fast
/// <see cref="BusinessException"/> ("gizli kayıp" kapanışı — eski davranış bilinmeyeni null'a düşürüp sessizce
/// MadeToOrder bırakıyordu). Eski 8-kovalı map'in BAYAT wire string'leri (<c>2020_2025</c>/<c>2006_2009</c>/
/// <c>2000_2005</c>/<c>before_2000</c>) canlı spec'te YOK → artık bilinmeyen sınıfında (bilinçli davranış
/// değişikliği, dry-run raporu); yaşayan eski eşlemeler (made_to_order/2010_2019/1990s/1980s) birebir korunur.</summary>
public class EtsyWhenMadeMapTests
{
    [Theory]
    [InlineData("made_to_order", ProductMadePeriod.MadeToOrder)]
    [InlineData("2020_2026", ProductMadePeriod.Y2020Plus)]
    [InlineData("2010_2019", ProductMadePeriod.Y2010To2019)]
    [InlineData("2007_2009", ProductMadePeriod.Y2007To2009)]
    [InlineData("before_2007", ProductMadePeriod.Before2007)]
    [InlineData("2000_2006", ProductMadePeriod.Y2000To2006)]
    [InlineData("1990s", ProductMadePeriod.Y1990s)]
    [InlineData("1980s", ProductMadePeriod.Y1980s)]
    [InlineData("1970s", ProductMadePeriod.Y1970s)]
    [InlineData("1960s", ProductMadePeriod.Y1960s)]
    [InlineData("1950s", ProductMadePeriod.Y1950s)]
    [InlineData("1940s", ProductMadePeriod.Y1940s)]
    [InlineData("1930s", ProductMadePeriod.Y1930s)]
    [InlineData("1920s", ProductMadePeriod.Y1920s)]
    [InlineData("1910s", ProductMadePeriod.Y1910s)]
    [InlineData("1900s", ProductMadePeriod.Y1900s)]
    [InlineData("1800s", ProductMadePeriod.Y1800s)]
    [InlineData("1700s", ProductMadePeriod.Y1700s)]
    [InlineData("before_1700", ProductMadePeriod.Before1700)]
    public void Maps_all_19_openapi_wire_values(string wire, ProductMadePeriod expected)
    {
        EtsyProductClient.MapWhenMade(wire).ShouldBe(expected);
    }

    [Theory]
    [InlineData("MADE_TO_ORDER", ProductMadePeriod.MadeToOrder)]
    [InlineData("  2020_2026  ", ProductMadePeriod.Y2020Plus)]
    public void Maps_case_insensitive_and_trimmed(string wire, ProductMadePeriod expected)
    {
        EtsyProductClient.MapWhenMade(wire).ShouldBe(expected);
    }

    // Kronolojik yeniden-numaralama pinli — DB'ye yazılan sayısal değerler (K9 kararı; delikli/legacy numaralama YOK).
    [Fact]
    public void Chronological_numbering_is_pinned()
    {
        ((int)ProductMadePeriod.MadeToOrder).ShouldBe(0);
        ((int)ProductMadePeriod.Y2020Plus).ShouldBe(1);
        ((int)ProductMadePeriod.Y2010To2019).ShouldBe(2);
        ((int)ProductMadePeriod.Y2007To2009).ShouldBe(3);
        ((int)ProductMadePeriod.Before2007).ShouldBe(4);
        ((int)ProductMadePeriod.Y2000To2006).ShouldBe(5);
        ((int)ProductMadePeriod.Y1990s).ShouldBe(6);
        ((int)ProductMadePeriod.Y1980s).ShouldBe(7);
        ((int)ProductMadePeriod.Y1970s).ShouldBe(8);
        ((int)ProductMadePeriod.Y1960s).ShouldBe(9);
        ((int)ProductMadePeriod.Y1950s).ShouldBe(10);
        ((int)ProductMadePeriod.Y1940s).ShouldBe(11);
        ((int)ProductMadePeriod.Y1930s).ShouldBe(12);
        ((int)ProductMadePeriod.Y1920s).ShouldBe(13);
        ((int)ProductMadePeriod.Y1910s).ShouldBe(14);
        ((int)ProductMadePeriod.Y1900s).ShouldBe(15);
        ((int)ProductMadePeriod.Y1800s).ShouldBe(16);
        ((int)ProductMadePeriod.Y1700s).ShouldBe(17);
        ((int)ProductMadePeriod.Before1700).ShouldBe(18);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_value_returns_null_so_product_default_is_kept(string? wire)
    {
        EtsyProductClient.MapWhenMade(wire).ShouldBeNull();
    }

    [Theory]
    [InlineData("2020_2025")]     // bayat eski wire (Etsy artık göndermiyor)
    [InlineData("2006_2009")]     // bayat eski wire
    [InlineData("2000_2005")]     // bayat eski wire
    [InlineData("before_2000")]   // bayat eski wire
    [InlineData("2020_2027")]     // gelecekteki rolling kova — map satırı güncellenmeden sessiz geçmesin
    [InlineData("garbage")]
    public void Unknown_value_fails_fast_instead_of_silent_null(string wire)
    {
        var exception = Should.Throw<BusinessException>(() => EtsyProductClient.MapWhenMade(wire));

        exception.Code.ShouldBe("TradeXpress:Etsy:Product:UnknownWhenMade");
        exception.Data["value"].ShouldBe(wire);
    }
}
