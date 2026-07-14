using System;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary><see cref="N11OrderClient.ParseOrderDetail"/> saf parse birim testleri — N11 SOAP ref v4.6 field yollarından
/// türetilmiş OrderDetailResponse XML'i. Alıcı + fatura/teslimat adresi + billingTemplate tutar kırılımı + itemList
/// (komisyon/indirim/kargo/nitelik) doğru kanal-agnostik <see cref="OrderDetailSnapshot"/>'a düşüyor mu doğrular. Ağ yok.</summary>
public class N11OrderDetailParseTests
{
    private static readonly DateTime FetchedAt = new(2026, 7, 11, 9, 0, 0, DateTimeKind.Utc);

    private const string SampleXml = """
        <GetOrderDetailResponse xmlns="http://www.n11.com/ws/schemas">
          <result><status>success</status></result>
          <orderDetail>
            <id>167373991</id>
            <orderNumber>202739833194</orderNumber>
            <citizenshipId>12345678901</citizenshipId>
            <createDate>05/07/2026 14:30</createDate>
            <invoiceType>1</invoiceType>
            <paymentType>Kredi Kartı</paymentType>
            <buyer>
              <fullName>AYŞE YILMAZ</fullName>
              <email>ayse@example.com</email>
              <tcId>11111111111</tcId>
              <taxId>2222222</taxId>
              <taxOffice>Kadıköy VD</taxOffice>
            </buyer>
            <billingAddress>
              <fullName>AYŞE YILMAZ</fullName>
              <address>Moda Cad. No 5</address>
              <neighborhood>Caferağa</neighborhood>
              <district>Kadıköy</district>
              <city>İstanbul</city>
              <postalCode>34710</postalCode>
              <gsm>5551112233</gsm>
            </billingAddress>
            <shippingAddress>
              <fullName>AYŞE YILMAZ</fullName>
              <address>Bağdat Cad. No 10</address>
              <district>Maltepe</district>
              <city>İstanbul</city>
            </shippingAddress>
            <billingTemplate>
              <originalPrice>300.00</originalPrice>
              <dueAmount>250.50</dueAmount>
              <sellerInvoiceAmount>40.00</sellerInvoiceAmount>
              <totalMallDiscountPrice>10.00</totalMallDiscountPrice>
              <totalSellerDiscount>5.00</totalSellerDiscount>
            </billingTemplate>
            <itemList>
              <item>
                <id>77</id>
                <productId>P-1</productId>
                <productName>Altın Kolye</productName>
                <productSellerCode>SKU-A</productSellerCode>
                <stockKeepingUnitId>SKU-99</stockKeepingUnitId>
                <quantity>2</quantity>
                <price>150.00</price>
                <commission>12.50</commission>
                <mallDiscount>10.00</mallDiscount>
                <sellerDiscount>5.00</sellerDiscount>
                <status>10</status>
                <approvedDate>05/07/2026 15:00</approvedDate>
                <shippingDate>06/07/2026 09:00</shippingDate>
                <shipmentInfo>
                  <campaignNumber>794303382243176</campaignNumber>
                  <campaignNumberStatus>1</campaignNumberStatus>
                  <shipmentCode>49908717</shipmentCode>
                  <shipmentMethod>1</shipmentMethod>
                  <trackingNumber>805078006980</trackingNumber>
                  <shipmentCompany><id>344</id><name>Yurtiçi Kargo</name><shortName>YK</shortName></shipmentCompany>
                </shipmentInfo>
                <attributes>
                  <attribute><name>Renk</name><value>Sarı</value></attribute>
                  <attribute><name>Ayar</name><value>14K</value></attribute>
                </attributes>
                <customTextOptionValues>
                  <customTextOptionValue><option>mürekkep rengi</option><text>SİYAH</text></customTextOptionValue>
                  <customTextOptionValue><option>yazılacak yazı</option><text>Örnek Kaşe Metni</text></customTextOptionValue>
                </customTextOptionValues>
              </item>
            </itemList>
          </orderDetail>
        </GetOrderDetailResponse>
        """;

    [Fact]
    public void Parses_buyer_invoice_and_payment()
    {
        var detail = N11OrderClient.ParseOrderDetail(XDocument.Parse(SampleXml), FetchedAt);

        detail.ShouldNotBeNull();
        detail!.InvoiceType.ShouldBe(1);
        detail.PaymentType.ShouldBe("Kredi Kartı");
        detail.CitizenshipId.ShouldBe("12345678901");
        detail.FetchedAt.ShouldBe(FetchedAt);

        var buyer = detail.Buyer.ShouldNotBeNull();
        buyer.FullName.ShouldBe("AYŞE YILMAZ");
        buyer.Email.ShouldBe("ayse@example.com");
        buyer.TcId.ShouldBe("11111111111");
        buyer.TaxId.ShouldBe("2222222");
        buyer.TaxOffice.ShouldBe("Kadıköy VD");
    }

    [Fact]
    public void Parses_billing_and_shipping_addresses()
    {
        var detail = N11OrderClient.ParseOrderDetail(XDocument.Parse(SampleXml), FetchedAt)!;

        var billing = detail.BillingAddress.ShouldNotBeNull();
        billing.Line.ShouldBe("Moda Cad. No 5");
        billing.Neighborhood.ShouldBe("Caferağa");
        billing.District.ShouldBe("Kadıköy");
        billing.City.ShouldBe("İstanbul");
        billing.PostalCode.ShouldBe("34710");
        billing.Gsm.ShouldBe("5551112233");

        var shipping = detail.ShippingAddress.ShouldNotBeNull();
        shipping.Line.ShouldBe("Bağdat Cad. No 10");
        shipping.District.ShouldBe("Maltepe");
        shipping.City.ShouldBe("İstanbul");
    }

    [Fact]
    public void Parses_billing_template_totals()
    {
        var totals = N11OrderClient.ParseOrderDetail(XDocument.Parse(SampleXml), FetchedAt)!.Totals.ShouldNotBeNull();

        totals.OriginalPrice.ShouldBe(300.00m);
        totals.DueAmount.ShouldBe(250.50m);
        totals.SellerInvoiceAmount.ShouldBe(40.00m);
        totals.TotalMallDiscountPrice.ShouldBe(10.00m);
        totals.TotalSellerDiscount.ShouldBe(5.00m);
    }

    [Fact]
    public void Parses_items_with_commission_shipment_and_attributes()
    {
        var detail = N11OrderClient.ParseOrderDetail(XDocument.Parse(SampleXml), FetchedAt)!;

        detail.Items.Count.ShouldBe(1);
        var item = detail.Items[0];
        item.ProductName.ShouldBe("Altın Kolye");
        item.ProductSellerCode.ShouldBe("SKU-A");
        item.SkuId.ShouldBe("SKU-99");
        item.Quantity.ShouldBe(2m);
        item.Price.ShouldBe(150.00m);
        item.Commission.ShouldBe(12.50m);
        item.MallDiscount.ShouldBe(10.00m);
        item.SellerDiscount.ShouldBe(5.00m);
        item.Status.ShouldBe("10");
        item.ShipmentCompany.ShouldBe("Yurtiçi Kargo");
        item.ShipmentMethod.ShouldBe(1);
        // TAM shipmentInfo alanları (item→shipmentInfo detay için)
        item.ShipmentCompanyId.ShouldBe("344");
        item.ShipmentCompanyShortName.ShouldBe("YK");
        item.ShipmentCode.ShouldBe("49908717");
        item.TrackingNumber.ShouldBe("805078006980");
        item.CampaignNumber.ShouldBe("794303382243176");
        item.CampaignNumberStatus.ShouldBe("1");
        // N11 CANLI alan adları: approvedDate/shippingDate (GMT+3 → UTC). Yanlış adla (approveDate/shipmentDate) null kalırdı.
        item.ApproveDate.ShouldBe(new DateTime(2026, 7, 5, 12, 0, 0, DateTimeKind.Utc));   // 15:00 GMT+3 → 12:00 UTC
        item.ShipmentDate.ShouldBe(new DateTime(2026, 7, 6, 6, 0, 0, DateTimeKind.Utc));   // 09:00 GMT+3 → 06:00 UTC

        item.Attributes.Count.ShouldBe(2);
        item.Attributes[0].Name.ShouldBe("Renk");
        item.Attributes[0].Value.ShouldBe("Sarı");
        item.Attributes[1].Name.ShouldBe("Ayar");
        item.Attributes[1].Value.ShouldBe("14K");

        // Alıcının özel metinleri (kaşe/mühür — customTextOptionValues)
        item.CustomTexts.Count.ShouldBe(2);
        item.CustomTexts[0].Option.ShouldBe("mürekkep rengi");
        item.CustomTexts[0].Text.ShouldBe("SİYAH");
        item.CustomTexts[1].Option.ShouldBe("yazılacak yazı");
        item.CustomTexts[1].Text.ShouldBe("Örnek Kaşe Metni");
    }

    [Fact]
    public void Missing_order_detail_element_yields_null()
    {
        var xml = """<GetOrderDetailResponse xmlns="http://www.n11.com/ws/schemas"><result><status>failure</status></result></GetOrderDetailResponse>""";
        N11OrderClient.ParseOrderDetail(XDocument.Parse(xml), FetchedAt).ShouldBeNull();
    }
}
