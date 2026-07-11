using System;
using System.Linq;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// <see cref="TrendyolOrderClient.ParseShipmentPackagesPage"/> birim testleri — örnek sevkiyat paketi (sipariş) JSON'u
/// → record'lar (saf parse; ağ/DI yok). Alan adları Trendyol V2 sevkiyat paketi yanıtına göre defansif okunur
/// (orderDate epoch-ms → UTC; grossAmount/totalPrice; müşteri ad+soyad birleşimi; kargo takip sayı/metin).
/// </summary>
public class TrendyolOrderClientParseTests
{
    private const string SamplePayload = """
    {
      "totalElements": 2,
      "totalPages": 1,
      "page": 0,
      "size": 200,
      "content": [
        {
          "id": 5551234,
          "shipmentPackageId": 5551234,
          "orderNumber": "TY-ORD-1",
          "orderDate": 1720000000000,
          "status": "Shipped",
          "cargoProviderName": "Yurtiçi Kargo",
          "cargoTrackingNumber": 998877665544,
          "customerFirstName": "Ayşe",
          "customerLastName": "Yılmaz",
          "grossAmount": 1299.90,
          "lines": [
            {
              "id": 111,
              "barcode": "BR-RED-1",
              "merchantSku": "STK-RED-1",
              "productName": "Deri Kılıf Kırmızı",
              "quantity": 2,
              "price": 649.95,
              "orderLineItemStatusName": "Shipped"
            },
            {
              "id": 112,
              "barcode": "BR-BLUE-1",
              "merchantSku": "STK-BLUE-1",
              "productName": "Deri Kılıf Mavi",
              "quantity": 1,
              "price": 700.00
            }
          ]
        },
        {
          "shipmentPackageId": 5559999,
          "orderNumber": "TY-ORD-2",
          "orderDate": 1720100000000,
          "status": "Delivered",
          "lines": [
            { "quantity": 1, "amount": 250.0, "productName": "Kolye" }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void Parse_reads_envelope_and_orders_and_lines()
    {
        var page = TrendyolOrderClient.ParseShipmentPackagesPage(0, 200, SamplePayload);

        page.TotalPages.ShouldBe(1);
        page.TotalElements.ShouldBe(2);
        page.Items.Count.ShouldBe(2);

        var first = page.Items[0];
        first.RemoteOrderId.ShouldBe("5551234");
        first.OrderNumber.ShouldBe("TY-ORD-1");
        first.RemoteStatus.ShouldBe("Shipped");
        first.OrderDate.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1720000000000).UtcDateTime);
        first.OrderDate.Kind.ShouldBe(DateTimeKind.Utc);
        first.CustomerName.ShouldBe("Ayşe Yılmaz");
        first.CargoProvider.ShouldBe("Yurtiçi Kargo");
        first.CargoTrackingNumber.ShouldBe("998877665544");   // sayısal → string'e indirgenir
        first.TotalAmount.ShouldBe(1299.90m);                  // grossAmount

        first.Lines.Count.ShouldBe(2);
        var red = first.Lines[0];
        red.RemoteLineId.ShouldBe("111");
        red.Barcode.ShouldBe("BR-RED-1");
        red.StockCode.ShouldBe("STK-RED-1");
        red.ProductName.ShouldBe("Deri Kılıf Kırmızı");
        red.Quantity.ShouldBe(2m);
        red.UnitPrice.ShouldBe(649.95m);
        red.LineTotal.ShouldBe(1299.90m);                      // totalPrice yok → quantity × price
        red.RemoteLineStatus.ShouldBe("Shipped");

        // İkinci sipariş: tutar alanı yok → satır toplamından türetilir; müşteri alanları yok → null.
        var second = page.Items[1];
        second.RemoteOrderId.ShouldBe("5559999");
        second.RemoteStatus.ShouldBe("Delivered");
        second.CustomerName.ShouldBeNull();
        second.TotalAmount.ShouldBe(250.0m);                   // grossAmount/totalPrice yok → Σ satır (amount)
        second.Lines.Single().UnitPrice.ShouldBe(250.0m);      // price yok → amount
    }

    [Fact]
    public void Parse_of_malformed_payload_throws_friendly_error()
    {
        var ex = Should.Throw<BusinessException>(() => TrendyolOrderClient.ParseShipmentPackagesPage(3, 50, "{ bozuk json"));

        ex.Code.ShouldBe("TradeXpress:Trendyol:Order:ListParseFailed");
        ex.Data["page"].ShouldBe(3);
    }
}
