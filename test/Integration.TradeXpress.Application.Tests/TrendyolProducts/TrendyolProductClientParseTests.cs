using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// <see cref="TrendyolProductClient.ParseSellerProductsPage"/> + <see cref="TrendyolProductClient.GroupByProductMainId"/>
/// birim testleri — örnek listeleme JSON'u → record'lar (saf parse; ağ/DI yok). Alan adları Trendyol V2 listeleme
/// yanıtına göre (pimCategoryId/brand/productContentId defansif okunur).
/// </summary>
public class TrendyolProductClientParseTests
{
    private const string SamplePayload = """
    {
      "totalElements": 3,
      "totalPages": 1,
      "page": 0,
      "size": 200,
      "content": [
        {
          "id": 111,
          "approved": true,
          "onSale": true,
          "barcode": "BR-RED-1",
          "title": "iPhone 15 Kılıf Deri",
          "description": "Gerçek deri kılıf — el yapımı.",
          "productMainId": "MAIN-1",
          "pimCategoryId": 411,
          "categoryName": "Telefon Kılıfı",
          "brandId": 82,
          "brand": "MarkaX",
          "quantity": 7,
          "listPrice": 1500.50,
          "salePrice": 1299.90,
          "vatRate": 20,
          "dimensionalWeight": 0.5,
          "deliveryDuration": 2,
          "stockCode": "STK-RED-1",
          "productContentId": 987654,
          "images": [ { "url": "https://cdn.example.com/1.jpg" }, { "url": "https://cdn.example.com/2.jpg" } ],
          "attributes": [
            { "attributeId": 47, "attributeName": "Renk", "attributeValueId": 686234, "attributeValue": "Kırmızı" },
            { "attributeId": 338, "attributeName": "Materyal", "customAttributeValue": "Deri" }
          ]
        },
        {
          "barcode": "BR-BLUE-1",
          "title": "iPhone 15 Kılıf Deri",
          "productMainId": "MAIN-1",
          "quantity": 3,
          "salePrice": 1349.90,
          "stockCode": "STK-BLUE-1",
          "approved": false
        },
        {
          "title": "Barkodsuz kalem — BOŞ barcode ile taşınır (import atla+raporla yapar)",
          "quantity": 1
        }
      ]
    }
    """;

    [Fact]
    public void Parse_reads_page_envelope_and_items_and_keeps_barcodeless_rows_with_empty_barcode()
    {
        var page = TrendyolProductClient.ParseSellerProductsPage(0, 200, SamplePayload);

        page.TotalPages.ShouldBe(1);
        page.TotalElements.ShouldBe(3);
        page.Items.Count.ShouldBe(3);   // barkodsuz üçüncü kalem BOŞ barcode ile taşınır — import raporlar (sessiz kayıp yok)
        page.Items[2].Variants.Single().Barcode.ShouldBe(string.Empty);

        var first = page.Items[0];
        first.Title.ShouldBe("iPhone 15 Kılıf Deri");
        first.Description.ShouldBe("Gerçek deri kılıf — el yapımı.");
        first.ProductMainId.ShouldBe("MAIN-1");
        first.CategoryId.ShouldBe("411");           // pimCategoryId sayısal → string'e indirgenir
        first.CategoryName.ShouldBe("Telefon Kılıfı");
        first.BrandId.ShouldBe("82");
        first.BrandName.ShouldBe("MarkaX");
        first.VatRate.ShouldBe(20);
        first.DimensionalWeight.ShouldBe(0.5m);
        first.DeliveryDuration.ShouldBe(2);
        first.ImageUrls.ShouldBe(new[] { "https://cdn.example.com/1.jpg", "https://cdn.example.com/2.jpg" });

        var variant = first.Variants.ShouldHaveSingleItem();
        variant.Barcode.ShouldBe("BR-RED-1");
        variant.StockCode.ShouldBe("STK-RED-1");
        variant.Quantity.ShouldBe(7);
        variant.ListPrice.ShouldBe(1500.50m);
        variant.SalePrice.ShouldBe(1299.90m);
        variant.ProductContentId.ShouldBe(987654);
        variant.Approved.ShouldBe(true);
        variant.OnSale.ShouldBe(true);

        variant.Attributes.Count.ShouldBe(2);
        variant.Attributes[0].AttributeId.ShouldBe(47);
        variant.Attributes[0].AttributeValueId.ShouldBe(686234);
        variant.Attributes[0].AttributeValue.ShouldBe("Kırmızı");
        variant.Attributes[1].AttributeId.ShouldBe(338);
        variant.Attributes[1].AttributeValueId.ShouldBeNull();
        variant.Attributes[1].CustomValue.ShouldBe("Deri");

        // Eksik alanlar null'a düşer (fail değil).
        var second = page.Items[1];
        second.Variants.Single().ListPrice.ShouldBeNull();
        second.Variants.Single().OnSale.ShouldBeNull();
        second.Variants.Single().Approved.ShouldBe(false);
        second.CategoryId.ShouldBeNull();
    }

    [Fact]
    public void GroupByProductMainId_merges_items_of_same_main_id_into_one_product()
    {
        var page = TrendyolProductClient.ParseSellerProductsPage(0, 200, SamplePayload);

        var grouped = TrendyolProductClient.GroupByProductMainId(page.Items);

        grouped.Count.ShouldBe(2);                      // MAIN-1 grubu + barkodsuz (mainId'siz) kendi başına kalem
        var product = grouped[0];                       // iki kalem aynı MAIN-1 grubunda
        product.ProductMainId.ShouldBe("MAIN-1");
        product.Variants.Select(v => v.Barcode).ShouldBe(new[] { "BR-RED-1", "BR-BLUE-1" });
        product.ImageUrls.Count.ShouldBe(2);            // ortak alanlar İLK kalemden
    }

    // Bozuk gövde SESSİZCE boş sayfa dönmez (o ve sonraki sayfaların kalemleri raporsuz kaybolurdu) —
    // dostane hatayla import durur; upsert-only olduğundan yeniden deneme güvenlidir.
    [Fact]
    public void Parse_of_malformed_payload_throws_friendly_error_instead_of_silent_partial_result()
    {
        var ex = Should.Throw<BusinessException>(() => TrendyolProductClient.ParseSellerProductsPage(3, 50, "{ bozuk json"));

        ex.Code.ShouldBe("TradeXpress:Trendyol:Product:ListParseFailed");
        ex.Data["page"].ShouldBe(3);
    }
}
