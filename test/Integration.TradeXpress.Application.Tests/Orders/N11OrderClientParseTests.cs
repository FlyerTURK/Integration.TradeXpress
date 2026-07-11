using System;
using System.Linq;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary><see cref="N11OrderClient.ParseOrders"/> saf parse birim testleri — WSDL yapısından üretilmiş örnek
/// DetailedOrderListResponse XML'i (canlı hesap boş olduğundan). Kalem-merkezli model order düzeyine düzleşiyor mu,
/// order-kargo ilk kalemden mi, birim fiyat = price/quantity mi, createDate GMT+3 → UTC mi doğrular. Ağ yok.</summary>
public class N11OrderClientParseTests
{
    // Gerçek N11 DetailedOrderListResponse yapısından türetilmiş (canlı doğrulandı 2026-07-11): buyer/fullName order
    // düzeyinde, kargo firması shipmentInfo/shipmentCompany/name (shipmentMethod bir KOD), order-status 5.
    private const string SampleXml = """
        <DetailedOrderListResponse xmlns="http://www.n11.com/ws/schemas">
          <result><status>success</status></result>
          <pagingData><currentPage>0</currentPage><pageSize>100</pageSize><totalCount>1</totalCount><pageCount>1</pageCount></pagingData>
          <orderList>
            <order>
              <buyer><fullName>AYŞEGÜL BİLGE</fullName><id>89021</id></buyer>
              <id>136043971</id>
              <orderNumber>201266339291</orderNumber>
              <status>5</status>
              <totalAmount>250.50</totalAmount>
              <createDate>05/07/2026 14:30</createDate>
              <orderItemList>
                <orderItem>
                  <id>77</id>
                  <productSellerCode>SKU-A</productSellerCode>
                  <productName>Altın Kolye</productName>
                  <quantity>2</quantity>
                  <price>200.00</price>
                  <status>10</status>
                  <shipmentInfo>
                    <trackingNumber>TRK-1</trackingNumber>
                    <shipmentMethod>1</shipmentMethod>
                    <shipmentCompany><id>344</id><name>Yurtiçi Kargo</name><shortName>YK</shortName></shipmentCompany>
                  </shipmentInfo>
                </orderItem>
                <orderItem>
                  <id>78</id>
                  <productSellerCode>SKU-B</productSellerCode>
                  <productName>Yüzük</productName>
                  <quantity>1</quantity>
                  <price>50.50</price>
                  <status>10</status>
                </orderItem>
              </orderItemList>
            </order>
          </orderList>
        </DetailedOrderListResponse>
        """;

    [Fact]
    public void Parses_order_flattening_items_and_first_item_shipment()
    {
        var orders = N11OrderClient.ParseOrders(XDocument.Parse(SampleXml));

        orders.Count.ShouldBe(1);
        var order = orders[0];
        order.RemoteOrderId.ShouldBe("136043971");
        order.OrderNumber.ShouldBe("201266339291");
        order.RemoteStatus.ShouldBe("5");
        order.CustomerName.ShouldBe("AYŞEGÜL BİLGE");      // buyer/fullName (order düzeyi)
        order.TotalAmount.ShouldBe(250.50m);
        order.CargoProvider.ShouldBe("Yurtiçi Kargo");     // shipmentInfo/shipmentCompany/name (shipmentMethod DEĞİL)
        order.CargoTrackingNumber.ShouldBe("TRK-1");       // ilk kalemin trackingNumber'ı
        // createDate 05/07/2026 14:30 (GMT+3) → 11:30 UTC
        order.OrderDate.ShouldBe(new DateTime(2026, 7, 5, 11, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Parses_lines_with_unit_price_derived_from_quantity()
    {
        var order = N11OrderClient.ParseOrders(XDocument.Parse(SampleXml)).Single();

        order.Lines.Count.ShouldBe(2);

        var a = order.Lines[0];
        a.StockCode.ShouldBe("SKU-A");
        a.ProductName.ShouldBe("Altın Kolye");
        a.Quantity.ShouldBe(2m);
        a.LineTotal.ShouldBe(200.00m);
        a.UnitPrice.ShouldBe(100.00m);                     // price / quantity
        a.Barcode.ShouldBeNull();                          // N11 kalemde barkod yok

        var b = order.Lines[1];
        b.StockCode.ShouldBe("SKU-B");
        b.Quantity.ShouldBe(1m);
        b.LineTotal.ShouldBe(50.50m);
        b.UnitPrice.ShouldBe(50.50m);
    }

    [Fact]
    public void Empty_order_list_yields_no_orders()
    {
        var xml = """<DetailedOrderListResponse xmlns="http://www.n11.com/ws/schemas"><orderList/></DetailedOrderListResponse>""";
        N11OrderClient.ParseOrders(XDocument.Parse(xml)).ShouldBeEmpty();
    }

    [Fact]
    public void Status_5_maps_to_delivered()
    {
        // Canlı doğrulandı: order-status 5 = tamamlanmış/teslim.
        N11OrderStatusMapper.Map("5").ShouldBe(OrderStatus.Delivered);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData(null)]
    [InlineData("abc")]
    public void Status_mapper_is_conservative_unknown_for_unseen_codes(string? raw)
    {
        // Henüz gözlenmemiş kodlar ihtiyatlı: Unknown (ham değer RemoteStatus'te korunur).
        N11OrderStatusMapper.Map(raw).ShouldBe(OrderStatus.Unknown);
    }
}
