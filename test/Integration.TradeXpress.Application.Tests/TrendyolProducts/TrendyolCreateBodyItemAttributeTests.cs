using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// TRENDYOL CREATE BODY'SİNDE ITEM-DÜZEYİ ATTRIBUTE — <see cref="TrendyolProductClient.BuildCreateBody"/>.
///
/// <para><b>Çivilenen hata:</b> body ürün-seviyesi nitelikleri her item'a AYNEN kopyalıyordu; item'ın kendi
/// (eksen) niteliği diye bir kavram yoktu. Çok varyantlı üründe iki sonuçtan biri yaşanıyordu: ya ilk varyantın
/// eksen değeri ("Kırmızı") TÜM varyantlara gidiyordu (eski import ürün seviyesine yazarken) ya da eksen beyanı
/// push'a HİÇ girmiyordu (import düzeltmesinden sonra). Artık her item ürün-seviyesi + KENDİ eksen değerleriyle
/// gider; aynı attributeId'de kalem kazanır (özgül olan geneli yener).</para>
/// </summary>
public class TrendyolCreateBodyItemAttributeTests
{
    private static TrendyolProductData BuildData(params TrendyolProductItem[] items)
    {
        return new TrendyolProductData(
            ProductMainId: "MAIN-1",
            Title: "Deri Kılıf",
            Description: "Gövde testi için açıklama.",
            CategoryId: "411",
            BrandId: "82",
            VatRate: 20,
            DimensionalWeight: null,
            DeliveryDuration: 2,
            FastDeliveryType: null,
            ImageUrls: Array.Empty<string>(),
            Attributes: new List<TrendyolAttributeValue> { new(60, 1001, null) },   // Materyal=Deri (ürün seviyesi)
            Items: items,
            SentMediaIds: Array.Empty<Guid>());
    }

    private static List<(int AttributeId, int? ValueId, string? Custom)> ItemAttributes(string body, int index)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("items")[index].GetProperty("attributes")
            .EnumerateArray()
            .Select(a => (
                a.GetProperty("attributeId").GetInt32(),
                a.TryGetProperty("attributeValueId", out var v) ? v.GetInt32() : (int?)null,
                a.TryGetProperty("customAttributeValue", out var c) ? c.GetString() : null))
            .ToList();
    }

    [Fact]
    public void Each_item_carries_product_level_plus_its_own_axis_attributes()
    {
        var body = TrendyolProductClient.BuildCreateBody(BuildData(
            new TrendyolProductItem("BR-RED", "STK", 5, 120m, 100m,
                new List<TrendyolAttributeValue> { new(47, 686234, null) }),
            new TrendyolProductItem("BR-BLUE", "STK", 3, 120m, 100m,
                new List<TrendyolAttributeValue> { new(47, 686240, null) })));

        var red = ItemAttributes(body, 0);
        red.ShouldContain((60, (int?)1001, (string?)null));    // ürün seviyesi taşınır
        red.ShouldContain((47, (int?)686234, (string?)null));  // kalemin KENDİ ekseni

        var blue = ItemAttributes(body, 1);
        blue.ShouldContain((60, (int?)1001, (string?)null));
        blue.ShouldContain((47, (int?)686240, (string?)null)); // iki kalem FARKLI eksen değeri taşır
        blue.ShouldNotContain(a => a.AttributeId == 47 && a.ValueId == 686234);
    }

    [Fact]
    public void Item_attribute_wins_over_product_level_on_same_attribute_id()
    {
        var body = TrendyolProductClient.BuildCreateBody(BuildData(
            new TrendyolProductItem("BR-1", "STK", 5, 120m, 100m,
                new List<TrendyolAttributeValue> { new(60, 2002, null) })));

        var attributes = ItemAttributes(body, 0);
        attributes.Count(a => a.AttributeId == 60).ShouldBe(1);   // dublike üretilmez
        attributes.ShouldContain((60, (int?)2002, (string?)null)); // kalem kazanır
    }

    [Fact]
    public void Item_without_own_attributes_keeps_the_old_body_shape()
    {
        var body = TrendyolProductClient.BuildCreateBody(BuildData(
            new TrendyolProductItem("BR-1", "STK", 5, 120m, 100m)));

        var attributes = ItemAttributes(body, 0);
        attributes.ShouldBe(new List<(int, int?, string?)> { (60, 1001, null) });
    }

    [Fact]
    public void Custom_text_axis_value_is_emitted_as_custom_attribute_value()
    {
        var body = TrendyolProductClient.BuildCreateBody(BuildData(
            new TrendyolProductItem("BR-1", "STK", 5, 120m, 100m,
                new List<TrendyolAttributeValue> { new(75, null, "50 ml") })));

        ItemAttributes(body, 0).ShouldContain((75, (int?)null, (string?)"50 ml"));
    }
}
