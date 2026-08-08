using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// ÜRÜN İNDİRİMİNİN FİYATA UYGULANMASI — <see cref="ProductDiscountCalculator"/>.
///
/// <para>N11 indirimi bir ALAN olarak alıp kendisi yorumluyor; Trendyol'da öyle bir alan yok, hesabı biz
/// yapıyoruz. Bu testler o hesabın tek kaynak olduğunu ve iki tehlikeli kenarını pinler: <b>süresi dolmuş
/// kampanya</b> ve <b>fiyatı sıfıra düşüren indirim</b>. İkisi de sessiz para kaybıdır.</para>
/// </summary>
public class ProductDiscountCalculatorTests
{
    private static readonly DateTime Today = new(2026, 8, 7);

    [Fact]
    public void No_discount_returns_the_list_price_unchanged()
    {
        Resolve(1000m, ProductDiscountType.None, null).ShouldBe(1000m);
    }

    [Fact]
    public void Percentage_discount_reduces_the_price()
    {
        Resolve(1000m, ProductDiscountType.Percentage, 2m).ShouldBe(980m);
    }

    [Fact]
    public void Amount_discount_subtracts_the_value()
    {
        Resolve(1000m, ProductDiscountType.Amount, 150m).ShouldBe(850m);
    }

    /// <summary>Kuruşa yuvarlanır — pazaryerine üç ondalıklı fiyat gitmez.</summary>
    [Fact]
    public void Result_is_rounded_to_two_decimals()
    {
        Resolve(99.99m, ProductDiscountType.Percentage, 33m).ShouldBe(66.99m);
    }

    /// <summary>SÜRESİ DOLMUŞ kampanya uygulanmaz.
    /// <para>Bu kolun yokluğu en sinsi hatadır: N11'de kampanya kendiliğinden biter (tarihleri o yorumlar),
    /// Trendyol'da ise hesabı biz yaptığımız için indirim SONSUZA KADAR açık kalırdı — fiyat düşük ama ortada
    /// hiçbir hata görünmez.</para></summary>
    [Fact]
    public void Expired_window_does_not_apply_the_discount()
    {
        ProductDiscountCalculator.ResolveSalePrice(
            1000m, ProductDiscountType.Percentage, 20m,
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), Today)
            .ShouldBe(1000m);
    }

    [Fact]
    public void Future_window_does_not_apply_the_discount_yet()
    {
        ProductDiscountCalculator.ResolveSalePrice(
            1000m, ProductDiscountType.Percentage, 20m,
            new DateTime(2026, 9, 1), new DateTime(2026, 9, 30), Today)
            .ShouldBe(1000m);
    }

    /// <summary>Pencerenin İLK ve SON günü DAHİLDİR (kullanıcı "1–31 Ağustos" derken 31'i de kasteder).</summary>
    [Theory]
    [InlineData(2026, 8, 7)]    // ortada
    [InlineData(2026, 8, 1)]    // ilk gün
    [InlineData(2026, 8, 31)]   // son gün
    public void Window_boundaries_are_inclusive(int year, int month, int day)
    {
        ProductDiscountCalculator.ResolveSalePrice(
            1000m, ProductDiscountType.Percentage, 10m,
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), new DateTime(year, month, day))
            .ShouldBe(900m);
    }

    /// <summary>Tarihsiz indirim SÜREKLİDİR (Product.SetDiscount ikisini birden boş bırakmaya izin verir).</summary>
    [Fact]
    public void Discount_without_dates_is_always_active()
    {
        Resolve(1000m, ProductDiscountType.Amount, 100m).ShouldBe(900m);
    }

    /// <summary>Fiyatı SIFIRA ya da ALTINA düşüren indirim FIRLATIR — sessizce 0'a kırpmak kıymetli madeni
    /// bedava listelemek olurdu.</summary>
    [Theory]
    [InlineData(1000)]   // tam fiyat kadar indirim
    [InlineData(1500)]   // fiyattan büyük indirim
    public void Discount_that_wipes_out_the_price_throws(int discount)
    {
        Should.Throw<BusinessException>(() => Resolve(1000m, ProductDiscountType.Amount, discount))
            .Code.ShouldBe("TradeXpress:Product:DiscountExceedsPrice");
    }

    /// <summary>%100 indirim de aynı kapıya çarpar — yüzde kolu ayrı bir kaçak bırakmaz.</summary>
    [Fact]
    public void Hundred_percent_discount_throws_as_well()
    {
        Should.Throw<BusinessException>(() => Resolve(1000m, ProductDiscountType.Percentage, 100m))
            .Code.ShouldBe("TradeXpress:Product:DiscountExceedsPrice");
    }

    /// <summary>Bozuk/eksik değer sessizce 0 indirim sayılır — push'u kırmaz (indirim opsiyonel bir süstür,
    /// yokluğu satışı durdurmamalı).</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Missing_or_non_positive_value_means_no_discount(int? value)
    {
        Resolve(1000m, ProductDiscountType.Percentage, value).ShouldBe(1000m);
    }

    private static decimal Resolve(decimal listPrice, ProductDiscountType type, decimal? value)
    {
        return ProductDiscountCalculator.ResolveSalePrice(listPrice, type, value, null, null, Today);
    }
}
