using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Integration.TradeXpress.Mocks.N11;

/// <summary>
/// N11 SOAP <c>OrderService</c> taklidi — sipariş akışını hesap olmadan çalıştırabilmek için.
///
/// <para><b>Neden sipariş de gerekti:</b> sipariş kodu (çekim + kabul/red/kargo) yazıldı ama HİÇ doğrulanmadı.
/// Push döngüsü mock'landıktan sonra doğrulanamayan en büyük yüzey buydu.</para>
///
/// <para><b>XML sadakati beklendiğinden kolay:</b> gerçek istemcinin ayrıştırıcısı NAMESPACE ve SIRA AGNOSTİK
/// (<c>e.Name.LocalName</c> ile arıyor — <c>N11OrderClient:131</c>). Yani ad alanı önek oyunlarına girmeye gerek
/// yok; doğru ELEMENT ADLARINI doğru iç içelikte üretmek yeterli. Bu, Dilim 1'de SOAP'tan kaçınma gerekçesini
/// büyük ölçüde geçersiz kılıyor.</para>
///
/// <para><b>Başarı sözleşmesi:</b> istemci yanıtta <c>&lt;status&gt;success&lt;/status&gt;</c> arıyor; başka
/// her şey <c>errorMessage</c> ile birlikte reddedilmiş sayılıyor. Yazma uçlarında retry YOKTUR (çift-aksiyon
/// riski) — mock da bunu bilerek tek seferde kesin cevap verir.</para>
/// </summary>
public static class N11MockOrderEndpoint
{
    private const string SoapNs = "http://schemas.xmlsoap.org/soap/envelope/";

    /// <summary>SOAP sipariş ucunu haritalar. Tek adres, işlem AYIRIMI gövdenin kök elementinden yapılır
    /// (gerçek N11 de böyle: tüm operasyonlar aynı .wsdl adresine POST edilir).</summary>
    public static IEndpointRouteBuilder MapN11MockOrderEndpoint(
        this IEndpointRouteBuilder endpoints, N11MockStore store, N11MockOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(store);

        // Açık tip ZORUNLU — bkz. N11MockEndpoints'teki RequestDelegate tuzağı.
        Func<HttpContext, Task<IResult>> handler = ctx => HandleAsync(ctx, store, options);
        endpoints.MapPost("/ws/OrderService.wsdl", handler);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(HttpContext ctx, N11MockStore store, N11MockOptions options)
    {
        if (options.LatencyMs > 0)
        {
            await Task.Delay(options.LatencyMs);
        }

        var raw = await new StreamReader(ctx.Request.Body, Encoding.UTF8).ReadToEndAsync();
        XDocument request;
        try
        {
            request = XDocument.Parse(raw);
        }
        catch (System.Xml.XmlException)
        {
            return Soap(Fault("İstek gövdesi geçerli XML değil."));
        }

        // Operasyon adı: zarfın gövdesindeki İLK element (ör. DetailedOrderListRequest).
        var operation = request.Root?.Descendants()
            .FirstOrDefault(e => e.Name.LocalName.EndsWith("Request", StringComparison.Ordinal))?
            .Name.LocalName ?? string.Empty;

        return operation switch
        {
            "DetailedOrderListRequest" => Soap(await BuildOrderListAsync(store)),
            "OrderDetailRequest" => Soap(await BuildOrderDetailAsync(store, request)),
            "OrderItemAcceptRequest" => Soap(Ok("OrderItemAcceptResponse")),
            "OrderItemRejectRequest" => Soap(Ok("OrderItemRejectResponse")),
            "MakeOrderItemShipmentRequest" => Soap(Ok("MakeOrderItemShipmentResponse")),
            _ => Soap(Fault($"Tanınmayan operasyon: '{operation}'.")),
        };
    }

    // ── Sipariş listesi ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Mağazadaki ürünlerden TÜRETİLMİŞ sipariş listesi: her ürün için bir sipariş. Sipariş verisi ayrıca
    /// tutulmuyor — push edilen ürün neyse siparişi de ondan doğuyor, böylece senaryo tek yerden (ürün deposu)
    /// kuruluyor ve "önce push et, sonra sipariş gelsin" akışı doğal çalışıyor.</summary>
    private static async Task<XElement> BuildOrderListAsync(N11MockStore store)
    {
        var (products, _, _) = await store.QueryProductsAsync(0, 200, null, null);

        var response = new XElement("DetailedOrderListResponse",
            new XElement("status", "success"),
            new XElement("pagingData",
                new XElement("pageCount", 1),
                new XElement("totalCount", products.Count)),
            new XElement("pageCount", 1),
            new XElement("orderList", products.Select((p, i) => BuildOrder(p, i)).ToArray()));

        return response;
    }

    private static XElement BuildOrder(N11MockProduct product, int index)
    {
        var orderId = 5000000000L + index;
        var itemId = 6000000000L + index;
        var price = product.SalePrice ?? 0m;

        return new XElement("order",
            new XElement("id", orderId),
            new XElement("orderNumber", $"MOCK-{orderId}"),
            new XElement("createDate", "01/08/2026 10:30"),
            new XElement("status", "Completed"),
            new XElement("totalAmount", Money(price)),
            new XElement("buyer",
                new XElement("fullName", "Mock Alıcı"),
                new XElement("email", "mock@example.invalid")),
            // ⚠ Kapsayıcı adı 'orderItemList' — 'itemList' DEĞİL (N11OrderClient:154,180 ikisini de bununla arıyor).
            // SKU alanı da 'productSellerCode'; 'sellerCode' yazılırsa satır gelir ama stok kodu SESSİZCE null kalır.
            new XElement("orderItemList",
                new XElement("orderItem",
                    new XElement("id", itemId),
                    new XElement("productId", product.N11ProductId),
                    new XElement("productName", product.Title ?? product.StockCode),
                    new XElement("productSellerCode", product.StockCode),
                    new XElement("quantity", 1),
                    new XElement("price", Money(price)),
                    new XElement("dueAmount", Money(price)),
                    new XElement("commission", Money(Math.Round(price * 0.10m, 2))),
                    new XElement("status", "Completed"),
                    new XElement("shipmentInfo",
                        new XElement("shipmentCompany",
                            new XElement("id", 7),
                            new XElement("name", "Mock Kargo")),
                        new XElement("trackingNumber", $"TRK{orderId}"),
                        new XElement("shipmentMethod", 1)))));
    }

    // ── Sipariş detayı ──────────────────────────────────────────────────────────────────────────────

    private static async Task<XElement> BuildOrderDetailAsync(N11MockStore store, XDocument request)
    {
        var requestedId = request.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "id")?.Value.Trim();

        var (products, _, _) = await store.QueryProductsAsync(0, 200, null, null);
        if (products.Count == 0)
        {
            return Fault("Sipariş bulunamadı (sahte mağaza boş).");
        }

        // Kimlik listeyle AYNI kuraldan türetilir (5000000000 + sıra) → detay, listedeki siparişle eşleşir.
        var index = 0;
        if (long.TryParse(requestedId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            index = (int)Math.Clamp(id - 5000000000L, 0, products.Count - 1);
        }

        var order = BuildOrder(products[index], index);

        return new XElement("OrderDetailResponse",
            new XElement("status", "success"),
            new XElement("orderDetail",
                order.Elements(),
                new XElement("paymentType", "CreditCard"),
                new XElement("invoiceType", "Individual"),
                new XElement("citizenshipId", "11111111111"),
                new XElement("billingAddress",
                    new XElement("fullName", "Mock Alıcı"),
                    new XElement("address", "Mock Mahallesi No:1"),
                    new XElement("city", "İstanbul"),
                    new XElement("district", "Kadıköy"),
                    new XElement("neighborhood", "Mock"),
                    new XElement("postalCode", "34000")),
                new XElement("shippingAddress",
                    new XElement("fullName", "Mock Alıcı"),
                    new XElement("address", "Mock Mahallesi No:1"),
                    new XElement("city", "İstanbul"),
                    new XElement("district", "Kadıköy"),
                    new XElement("neighborhood", "Mock"),
                    new XElement("postalCode", "34000"))));
    }

    // ── Zarf yardımcıları ───────────────────────────────────────────────────────────────────────────

    private static XElement Ok(string responseName)
    {
        return new XElement(responseName, new XElement("status", "success"));
    }

    private static XElement Fault(string message)
    {
        // İstemci "status != success" görünce errorMessage'ı okuyup dostane hataya çeviriyor.
        return new XElement("ErrorResponse",
            new XElement("status", "failure"),
            new XElement("errorMessage", message));
    }

    /// <summary>SOAP zarfına sarar. Ad alanı ÖNEMSİZ (istemci LocalName ile arıyor) ama gerçekçi olsun diye
    /// standart soapenv kullanılıyor.</summary>
    private static IResult Soap(XElement body)
    {
        XNamespace ns = SoapNs;
        var envelope = new XDocument(
            new XElement(ns + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soapenv", SoapNs),
                new XElement(ns + "Body", body)));

        return Results.Content(envelope.ToString(), "text/xml; charset=utf-8");
    }

    private static string Money(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
