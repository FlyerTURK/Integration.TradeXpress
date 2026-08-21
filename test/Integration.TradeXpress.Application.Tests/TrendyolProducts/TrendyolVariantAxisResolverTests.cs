using System;
using System.Collections.Generic;
using System.Linq;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// <see cref="TrendyolVariantAxisResolver"/> — varyant ekseninin içe aktarılan veriden çözümü (saf birim).
///
/// <para><b>Neden bu çözüm meşru:</b> Trendyol'un kendi kuralı aynı <c>productMainId</c> altındaki kalemlerde
/// "yalnız attributes bölümünün farklılaşmasını" şart koşuyor. Dolayısıyla <i>kalemler arasında değişen
/// nitelik</i> ile <i>varyant ekseni</i> aynı şeydir; kategori tanımını ayrıca çekmek ikinci bir gerçek
/// kaynağı yaratırdı.</para>
///
/// <para><b>Çivilenen asıl hata:</b> içe aktarım ürün-seviyesi nitelikleri grubun İLK kaleminden alıyordu —
/// eksen varsa birinci varyantın değeri ürünün değeri sanılıyordu.</para>
/// </summary>
public class TrendyolVariantAxisResolverTests
{
    private const int Volume = 47;      // eksen olan nitelik (ör. Hacim)
    private const int Origin = 12;      // ürün seviyesi nitelik (ör. Menşei)

    [Fact]
    public void The_attribute_that_differs_between_items_is_the_variant_axis()
    {
        var plan = TrendyolVariantAxisResolver.Resolve(new[]
        {
            Variant("BC-1", Attr(Volume, "Hacim", 101, "50 ml"), Attr(Origin, "Menşei", 900, "Türkiye")),
            Variant("BC-2", Attr(Volume, "Hacim", 102, "100 ml"), Attr(Origin, "Menşei", 900, "Türkiye")),
        });

        plan.AxisAttributeIds.ShouldBe(new[] { Volume });
        plan.ValuesByBarcode["BC-1"].ShouldHaveSingleItem().ValueText.ShouldBe("50 ml");
        plan.ValuesByBarcode["BC-2"].ShouldHaveSingleItem().ValueText.ShouldBe("100 ml");
    }

    [Fact]
    public void Attributes_shared_by_every_item_stay_at_product_level()
    {
        // SABİTLENEN HATA: eskiden ürün nitelikleri ilk kalemden OLDUĞU GİBİ alınıyordu; eksen de içine
        // karışıyor ve birinci varyantın değeri ürüne yazılıyordu.
        var plan = TrendyolVariantAxisResolver.Resolve(new[]
        {
            Variant("BC-1", Attr(Volume, "Hacim", 101, "50 ml"), Attr(Origin, "Menşei", 900, "Türkiye")),
            Variant("BC-2", Attr(Volume, "Hacim", 102, "100 ml"), Attr(Origin, "Menşei", 900, "Türkiye")),
        });

        plan.ProductLevelAttributes.ShouldHaveSingleItem().AttributeId.ShouldBe(Origin);
        plan.ProductLevelAttributes.ShouldAllBe(a => a.AttributeId != Volume);
    }

    [Fact]
    public void A_single_item_group_has_no_axis()
    {
        // Karşılaştıracak ikinci kalem yok → hiçbir nitelik "değişen" sayılamaz. Tek varyantta ayırt edici
        // eksene ihtiyaç da yoktur; hepsi ürün seviyesinde kalır.
        var plan = TrendyolVariantAxisResolver.Resolve(new[]
        {
            Variant("BC-TEK", Attr(Volume, "Hacim", 101, "50 ml"), Attr(Origin, "Menşei", 900, "Türkiye")),
        });

        plan.AxisAttributeIds.ShouldBeEmpty();
        plan.ProductLevelAttributes.Count.ShouldBe(2);
    }

    [Fact]
    public void A_missing_attribute_counts_as_a_difference()
    {
        // Bir kalemde olup diğerinde OLMAYAN nitelik eksendir — yokluk da bir değerdir. Aksi halde
        // "kırmızı" ile "renksiz" aynı kovaya düşer ve iki varyant tek varyant sanılırdı.
        var plan = TrendyolVariantAxisResolver.Resolve(new[]
        {
            Variant("BC-1", Attr(Volume, "Hacim", 101, "50 ml")),
            Variant("BC-2"),
        });

        plan.AxisAttributeIds.ShouldBe(new[] { Volume });
    }

    [Fact]
    public void Free_text_values_can_also_form_an_axis()
    {
        // Trendyol customAttributeValue ile serbest değer kabul ediyor; karşılaştırma METİN üzerinden
        // yapıldığı için kimliksiz değerler de eksen olabilir.
        var plan = TrendyolVariantAxisResolver.Resolve(new[]
        {
            Variant("BC-1", Custom(Volume, "Hacim", "50 ml")),
            Variant("BC-2", Custom(Volume, "Hacim", "100 ml")),
        });

        plan.AxisAttributeIds.ShouldBe(new[] { Volume });
        plan.ValuesByBarcode["BC-2"].ShouldHaveSingleItem().AttributeValueId.ShouldBeNull();
        plan.ValuesByBarcode["BC-2"].ShouldHaveSingleItem().ValueText.ShouldBe("100 ml");
    }

    [Fact]
    public void Identical_items_produce_no_axis_at_all()
    {
        // CANLI ÖRNEK (8699459542258-01): iki kalem, hiç nitelik yok → ayırt edici eksen YOK. Bu ürün
        // bugünkü hâliyle Trendyol'a gönderilse iki kalem birbirinden ayrılamazdı; boş eksen bunu görünür kılar.
        var plan = TrendyolVariantAxisResolver.Resolve(new[]
        {
            Variant("8699459542258-02"),
            Variant("8699459542258"),
        });

        plan.AxisAttributeIds.ShouldBeEmpty();
        plan.ValuesByBarcode["8699459542258-02"].ShouldBeEmpty();
    }

    // ── Kurulum yardımcıları ────────────────────────────────────────────────────────────────────────

    private static TrendyolRemoteVariant Variant(string barcode, params TrendyolRemoteAttribute[] attributes)
    {
        return new TrendyolRemoteVariant(
            Barcode: barcode,
            StockCode: barcode,
            Quantity: 1,
            ListPrice: 100m,
            SalePrice: 100m,
            ProductContentId: 1,
            Approved: true,
            OnSale: true,
            Attributes: attributes.ToList());
    }

    private static TrendyolRemoteAttribute Attr(int id, string name, int valueId, string valueText)
    {
        return new TrendyolRemoteAttribute(id, name, valueId, valueText, null);
    }

    private static TrendyolRemoteAttribute Custom(int id, string name, string customValue)
    {
        return new TrendyolRemoteAttribute(id, name, null, null, customValue);
    }
}
