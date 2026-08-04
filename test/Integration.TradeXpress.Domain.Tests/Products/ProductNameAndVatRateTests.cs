using System;
using System.Linq;
using Integration.TradeXpress.Products;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>Ürün adı casing'i + KDV oranı kuralları (2026-08-03 Hakan kararları).
///
/// <para><b>Ad:</b> <see cref="Product.Name"/> pazaryerine BAŞLIK olarak gider ve kanal seviyesinde başlık
/// override'ı yoktur → TitleCase normalizasyonu KALDIRILDI. tr-TR kültüründe "iPhone" → "İphone" oluyordu ve
/// doğru başlık sistemde hiçbir yoldan üretilemiyordu. Diğer katalog entity'lerinden (Metal/Good/…) bilinçli
/// sapma: onların adı iç kullanım, bunun ki pazaryeri vitrini.</para>
///
/// <para><b>KDV:</b> oran ÜRÜNÜN özelliğidir (mevzuat mala bakar, kanala değil) ve <b>varsayılanı YOKTUR</b>.
/// Kuyumda kıymetli maden teslimi %0'dır (istisna faturası kesilir), işçilik %20'dir — yani "kuyum = %20"
/// varsayımı yanlıştır. Sessiz varsayılan = yanlış fatura + satıcıya rücu.</para></summary>
public class ProductNameAndVatRateTests
{
    private static Product NewProduct(string name = "Test Ürünü")
    {
        return new Product(Guid.NewGuid(), "TSTVAT", name);
    }

    // ── Ad casing ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("iPhone 15 Pro")]          // tr-TR TitleCase bunu "İphone 15 Pro" yapıyordu
    [InlineData("MacBook Air")]            // → "Macbook Air"
    [InlineData("adidas Originals")]       // → "Adidas Originals"
    [InlineData("LG OLED evo C4")]
    public void Should_Preserve_Brand_Casing_In_Name(string name)
    {
        NewProduct(name).Name.ShouldBe(name);
    }

    [Fact]
    public void SetName_Should_Preserve_Casing_On_Update_Too()
    {
        var product = NewProduct();

        product.SetName("iPhone 15 Pro Max");

        product.Name.ShouldBe("iPhone 15 Pro Max");
    }

    [Fact]
    public void SetName_Should_Still_Trim_And_Validate()
    {
        var product = NewProduct();

        product.SetName("   Boşluklu Ad   ");

        product.Name.ShouldBe("Boşluklu Ad");
        Should.Throw<BusinessException>(() => product.SetName("  "));
    }

    /// <summary>TitleCase yolu SİLİNMEDİ, yalnız varsayılan olmaktan çıktı — açıkça isteyen çağırabilir.</summary>
    [Fact]
    public void SetName_Should_Still_TitleCase_When_Explicitly_Requested()
    {
        var product = NewProduct();

        product.SetName("gümüş kolye", normalizeTitle: true);

        product.Name.ShouldBe("Gümüş Kolye");
    }

    // ── KDV oranı ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void New_Product_Should_Have_No_Default_Vat_Rate()
    {
        // Boş kalması KASITLI: kıymetli maden %0, işçilik %20 — "hep 20" varsayımı yanlış fatura üretir.
        NewProduct().VatRate.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]     // kıymetli maden teslimi — KDV istisna faturası
    [InlineData(1)]     // kitap/gazete
    [InlineData(10)]    // gıda/tekstil
    [InlineData(20)]    // genel oran + işçilik
    public void Should_Accept_Every_Statutory_Rate(int rate)
    {
        var product = NewProduct();

        product.SetVatRate(rate);

        product.VatRate.ShouldBe(rate);
    }

    [Theory]
    [InlineData(8)]     // 2023 öncesi indirimli oran
    [InlineData(18)]    // 2023 öncesi genel oran
    [InlineData(-1)]
    [InlineData(100)]
    public void Should_Reject_Rate_Outside_Statutory_Set(int rate)
    {
        var product = NewProduct();

        var ex = Should.Throw<BusinessException>(() => product.SetVatRate(rate));

        ex.Code.ShouldBe("TradeXpress:Product:VatRateInvalid");
        ex.Data["VatRate"].ShouldBe(rate);
        product.VatRate.ShouldBeNull();
    }

    [Fact]
    public void Should_Allow_Clearing_Vat_Rate()
    {
        var product = NewProduct();
        product.SetVatRate(0);

        product.SetVatRate(null);

        product.VatRate.ShouldBeNull();
    }

    /// <summary>Mevzuat kümesi ile N11'in kabul ettiği küme bugün AYNI; ayrı sabitler olarak tutuluyorlar
    /// (biri mevzuat, diğeri pazaryeri kaynaklı — biri değişirse diğeri değişmeyebilir). Bu test ikisinin
    /// bugünkü hizasını kilitler ki ayrışma sessizce olmasın.</summary>
    [Fact]
    public void Statutory_Set_Should_Be_The_Turkish_Rates()
    {
        ProductConsts.AllowedVatRates.OrderBy(rate => rate).ShouldBe(new[] { 0, 1, 10, 20 });
    }
}
