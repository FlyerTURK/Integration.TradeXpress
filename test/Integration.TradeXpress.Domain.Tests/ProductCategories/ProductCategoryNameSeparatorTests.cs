using System;
using Integration.TradeXpress.ProductCategories;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Kategori adında YOL AYRACI yasağı (2026-08-04 Hakan).
///
/// <para><b>Neden konvansiyon testi:</b> kategori yolu düz metin olarak kuruluyor — segmentler <c>›</c> ile
/// birleştiriliyor ("Takı › Yüzük › Alyans"). Ada ayraç karakteri girerse TEK bir kategori listede İKİ SEVİYE
/// gibi görünür; üstelik yol hesaplanmış bir alan olduğundan geri ayrıştırıp düzeltmek de mümkün değildir.
/// Kirlenme kayıt anında engellenmezse veri kalıcı olarak yanlış okunur.</para>
///
/// <para>ASCII <c>&gt;</c> de yasak: gerçek ayraçtan gözle ayırt edilemiyor, kullanıcı hangisini yazdığını
/// bilmek zorunda kalmamalı.</para>
/// </summary>
public class ProductCategoryNameSeparatorTests
{
    private static ProductCategory NewCategory(string name)
    {
        return new ProductCategory(Guid.NewGuid(), name, parentId: null);
    }

    [Theory]
    [InlineData("Takı › Yüzük")]      // gerçek ayraç
    [InlineData("Takı > Yüzük")]      // ASCII karşılığı
    [InlineData("›Yüzük")]
    [InlineData("Yüzük>")]
    public void Name_containing_a_path_separator_is_rejected(string name)
    {
        var ex = Should.Throw<BusinessException>(() => NewCategory(name));

        ex.Code.ShouldBe("TradeXpress:ProductCategory:NameHasPathSeparator");
    }

    [Theory]
    [InlineData("Yüzük")]
    [InlineData("Altın Takılar")]
    [InlineData("Pırlanta Set & Takım")]   // & serbest — ayraç değil
    [InlineData("22 Ayar Bilezik")]
    public void Ordinary_names_are_accepted(string name)
    {
        NewCategory(name).Name.ShouldBe(name);
    }

    /// <summary>Yasak yalnız KURULUŞTA değil, sonraki her ad değişikliğinde de geçerli — aksi hâlde kural
    /// düzenleme formundan atlatılabilirdi.</summary>
    [Fact]
    public void Renaming_into_a_separator_is_also_rejected()
    {
        var category = NewCategory("Yüzük");

        Should.Throw<BusinessException>(() => category.SetName("Takı › Yüzük"));

        category.Name.ShouldBe("Yüzük");   // reddedilen ad yazılmamalı
    }
}
