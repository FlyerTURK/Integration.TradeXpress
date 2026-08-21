using System;
using System.Linq;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary><see cref="N11OrderClient.ParseOrders"/> saf parse birim testleri — WSDL yapısından üretilmiş örnek
/// DetailedOrderListResponse XML'i (canlı hesap boş olduğundan). Kalem-merkezli model order düzeyine düzleşiyor mu,
/// order-kargo ilk kalemden mi, <c>price</c> BİRİM fiyat olarak mı okunuyor, createDate GMT+3 → UTC mi doğrular. Ağ yok.</summary>
public class N11OrderClientParseTests
{
    // Gerçek N11 DetailedOrderListResponse yapısından türetilmiş (canlı doğrulandı 2026-07-11): buyer/fullName order
    // düzeyinde, kargo firması shipmentInfo/shipmentCompany/name (shipmentMethod bir KOD), order-status 5.
    // totalAmount = Σ(price × quantity) = 200×2 + 50,50 = 450,50 — canlı 126 kalemde ÖLÇÜLDÜ (2026-08-07): N11'in
    // gönderdiği başlık toplamı, kalemdeki price'ın adetle çarpımına kuruşu kuruşuna eşit. price BİRİM fiyattır.
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
              <totalAmount>450.50</totalAmount>
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
        order.TotalAmount.ShouldBe(450.50m);
        order.CargoProvider.ShouldBe("Yurtiçi Kargo");     // shipmentInfo/shipmentCompany/name (shipmentMethod DEĞİL)
        order.CargoTrackingNumber.ShouldBe("TRK-1");       // ilk kalemin trackingNumber'ı
        // createDate 05/07/2026 14:30 (GMT+3) → 11:30 UTC
        order.OrderDate.ShouldBe(new DateTime(2026, 7, 5, 11, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Parses_lines_with_price_as_unit_price()
    {
        var order = N11OrderClient.ParseOrders(XDocument.Parse(SampleXml)).Single();

        order.Lines.Count.ShouldBe(2);

        var a = order.Lines[0];
        a.StockCode.ShouldBe("SKU-A");
        a.ProductName.ShouldBe("Altın Kolye");
        a.Quantity.ShouldBe(2m);
        a.UnitPrice.ShouldBe(200.00m);                     // price = BİRİM fiyat (bölünmez)
        a.LineTotal.ShouldBe(400.00m);                     // price × quantity
        a.Barcode.ShouldBeNull();                          // N11 kalemde barkod yok

        var b = order.Lines[1];
        b.StockCode.ShouldBe("SKU-B");
        b.Quantity.ShouldBe(1m);
        b.UnitPrice.ShouldBe(50.50m);
        b.LineTotal.ShouldBe(50.50m);                      // adet 1 → ikisi eşit
    }

    [Fact]
    public void Multi_quantity_line_maps_price_as_unit_price()
    {
        // Tek adetli vakada iki yorum da aynı sayıyı verir → hata YALNIZ adet>1'de görünür (canlıda 20 kalem).
        var xml = """
            <DetailedOrderListResponse xmlns="http://www.n11.com/ws/schemas">
              <orderList><order>
                <id>1</id><orderNumber>N-1</orderNumber><createDate>05/07/2026 14:30</createDate>
                <totalAmount>300.00</totalAmount>
                <orderItemList><orderItem>
                  <id>9</id><productSellerCode>SKU-C</productSellerCode><productName>Bilezik</productName>
                  <quantity>3</quantity><price>100.00</price>
                </orderItem></orderItemList>
              </order></orderList>
            </DetailedOrderListResponse>
            """;

        var line = N11OrderClient.ParseOrders(XDocument.Parse(xml)).Single().Lines.Single();

        line.Quantity.ShouldBe(3m);
        line.UnitPrice.ShouldBe(100.00m);
        line.LineTotal.ShouldBe(300.00m);
    }

    [Fact]
    public void Header_total_falls_back_to_sum_of_line_totals()
    {
        // totalAmount yoksa başlık satırlardan toplanır. LineTotal artık GERÇEK satır toplamı olduğundan bu
        // yedek yol da kendiliğinden doğrulanır (eskiden birim fiyatları toplayıp eksik başlık üretiyordu).
        var xml = SampleXml.Replace("<totalAmount>450.50</totalAmount>", string.Empty);

        N11OrderClient.ParseOrders(XDocument.Parse(xml)).Single().TotalAmount.ShouldBe(450.50m);
    }

    [Fact]
    public void Zero_quantity_line_keeps_price_and_is_not_dropped()
    {
        // Savunma kolu: adet 0 gelirse kalem KAYBEDİLMEZ, LineTotal price'a düşer (mevcut fail-open felsefesi
        // korunur — genişletilmez). Aksi hâlde bozuk tek kalem yüzünden sipariş sessizce eksik kaydolurdu.
        var xml = """
            <DetailedOrderListResponse xmlns="http://www.n11.com/ws/schemas">
              <orderList><order><id>2</id><orderNumber>N-2</orderNumber><createDate>05/07/2026 14:30</createDate>
                <orderItemList><orderItem><id>9</id><productSellerCode>SKU-D</productSellerCode>
                  <productName>Küpe</productName><quantity>0</quantity><price>75.00</price>
                </orderItem></orderItemList>
              </order></orderList>
            </DetailedOrderListResponse>
            """;

        var line = N11OrderClient.ParseOrders(XDocument.Parse(xml)).Single().Lines.Single();

        line.Quantity.ShouldBe(0m);
        line.UnitPrice.ShouldBe(75.00m);
        line.LineTotal.ShouldBe(75.00m);
    }

    /// <summary>SEED isteği tarih filtresi TAŞIMAZ — period gönderilseydi kanalın geçmişi gizlenirdi
    /// (canlı doğrulandı: period'suz 106 sipariş, period'lu 0).</summary>
    [Fact]
    public void Seed_request_carries_no_date_filter()
    {
        var body = BuildRequestBody(sinceUtc: null);

        body.Descendants().Any(e => e.Name.LocalName == "period").ShouldBeFalse();
    }

    /// <summary>DELTA isteği dar pencere taşır — dolu kanalı 2 dakikada bir tüm geçmişiyle taramak throttle
    /// bütçesini yakardı.</summary>
    [Fact]
    public void Delta_request_carries_the_window()
    {
        var body = BuildRequestBody(sinceUtc: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

        var period = body.Descendants().Single(e => e.Name.LocalName == "period");
        period.Elements().Single(e => e.Name.LocalName == "startDate").Value.ShouldBe("01/08/2026");
        period.Elements().Any(e => e.Name.LocalName == "endDate").ShouldBeTrue();
    }

    /// <summary>İstek body'sini yansımayla üretir — <c>BuildListRequest</c> private'tır ve öyle KALMALI
    /// (dışarıya açmak, iki stratejinin çağrı yerinde karışmasına davet olurdu).</summary>
    private static XElement BuildRequestBody(DateTime? sinceUtc)
    {
        var method = typeof(N11OrderClient).GetMethod(
            "BuildListRequest",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.ShouldNotBeNull();

        return (XElement)method!.Invoke(null, new object?[] { "key", "secret", 0, sinceUtc })!;
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
        // order-status 5 = Tamamlandı → Delivered (SOAP ref v4.6; kalem-status 10 + tracking/shippingDate).
        N11OrderStatusMapper.Map("5").ShouldBe(OrderStatus.Delivered);
    }

    [Theory]
    [InlineData("4")]      // Geçersiz — belirsiz, nötr duruma zorlanmaz
    [InlineData("99")]
    [InlineData(null)]
    [InlineData("abc")]
    public void Status_mapper_is_conservative_unknown_for_unseen_codes(string? raw)
    {
        // Bilinmeyen/geçersiz kodlar ihtiyatlı: Unknown (ham değer RemoteStatus'te korunur).
        N11OrderStatusMapper.Map(raw).ShouldBe(OrderStatus.Unknown);
    }
}
