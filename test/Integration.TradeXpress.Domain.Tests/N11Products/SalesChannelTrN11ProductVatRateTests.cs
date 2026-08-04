using System;
using System.Linq;
using Integration.TradeXpress.N11Products;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>KDV oranı kuralı — N11 serbest yüzde KABUL ETMEZ (resmî v9.0 REST dokümanı: kapalı küme 0/1/10/20).
///
/// <para><b>Neden mekanik ağ:</b> kuyumcuda oran ürüne göre değişir (külçe ≠ işçilikli mücevher). Yanlış oranla
/// push, N11'in müşteriye YANLIŞ fatura kesip farkı satıcıya rücu etmesi demektir → oran ne tahmin edilir ne de
/// "standart %20" varsayılır. İki koruma birlikte test edilir: (1) küme dışı oran DB'ye giremez, (2) alanın
/// VARSAYILANI YOKTUR — biri ileride "kolaylık olsun" diye 20 default'u koyarsa bu test kırmızıya döner.</para></summary>
public class SalesChannelTrN11ProductVatRateTests
{
    private static SalesChannelTrN11Product NewProduct()
    {
        return new SalesChannelTrN11Product(
            companyId: Guid.NewGuid(),
            salesChannelId: Guid.NewGuid(),
            productId: Guid.NewGuid(),
            sellerCode: "URUN01-1",
            sequenceNo: 1,
            categoryExternalId: "1001",
            shipmentTemplateName: "Sablon");
    }

    [Fact]
    public void New_Product_Should_Have_No_Default_Vat_Rate()
    {
        // Boş kalması KASITLI: push fail-fast reddetsin, sessizce yanlış oran gitmesin.
        NewProduct().VatRate.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(20)]
    public void Should_Accept_Every_Rate_N11_Allows(int rate)
    {
        var product = NewProduct();

        product.SetVatRate(rate);

        product.VatRate.ShouldBe(rate);
    }

    [Theory]
    [InlineData(8)]     // eski %8 oranı — 2023 öncesi alışkanlık, N11 artık kabul etmiyor
    [InlineData(18)]    // eski standart oran
    [InlineData(-1)]
    [InlineData(100)]
    public void Should_Reject_Rate_Outside_N11_Set(int rate)
    {
        var product = NewProduct();

        var ex = Should.Throw<BusinessException>(() => product.SetVatRate(rate));

        ex.Code.ShouldBe("TradeXpress:N11:Product:VatRateInvalid");
        ex.Data["VatRate"].ShouldBe(rate);
        product.VatRate.ShouldBeNull();   // reddedilen değer yazılmamalı
    }

    [Fact]
    public void Should_Allow_Clearing_Back_To_Unset()
    {
        var product = NewProduct();
        product.SetVatRate(20);

        product.SetVatRate(null);

        product.VatRate.ShouldBeNull();
    }

    /// <summary>Küme TEK kaynaktan okunur: entity guard'ı, REST istemci doğrulaması ve KDV combo'su aynı listeye
    /// bakar. Biri kopya bir liste tutmaya kalkarsa buradaki içerik iddiası ayrışmayı yakalar.</summary>
    [Fact]
    public void Allowed_Set_Should_Be_The_Closed_N11_Set()
    {
        N11ProductConsts.AllowedVatRates.OrderBy(rate => rate).ShouldBe(new[] { 0, 1, 10, 20 });
    }
}
