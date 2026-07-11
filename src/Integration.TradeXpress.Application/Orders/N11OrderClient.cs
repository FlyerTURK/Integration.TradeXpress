using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// <see cref="IN11OrderClient"/> — N11 SOAP <c>OrderService.DetailedOrderList</c> (salt-okuma). Auth + gönderim
/// N11ShipmentTemplateClient deseniyle AYNI; yanıt namespace/sıra AGNOSTİK parse edilir. Sipariş modeli KALEM-merkezli
/// (order → orderItemList → item) → order düzeyine düzleştirilir (kanal-agnostik <see cref="RemoteOrder"/>).
/// N11 bu ucu SIKI throttle'lar → &quot;belli süre&quot; hata mesajında bekleyip yeniden dener (sessiz kısmi sonuç YOK).
/// Sır (appSecret) loglanmaz.
/// </summary>
public sealed class N11OrderClient : IN11OrderClient, ITransientDependency
{
    private const string Endpoint = "https://api.n11.com/ws/OrderService.wsdl";
    private static readonly XNamespace Soapenv = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Sch = "http://www.n11.com/ws/schemas";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(40) };

    private const int PageSize = 100;
    private const int MaxPageLoops = 500;
    private const int MaxThrottleRetries = 5;
    private const int ThrottleWaitSeconds = 6;

    private readonly ILogger<N11OrderClient> _logger;

    public N11OrderClient(ILogger<N11OrderClient> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<RemoteOrder>> GetAllOrdersAsync(
        string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var all = new List<RemoteOrder>();

        var page = 0;
        int pageCount;
        do
        {
            var doc = await PostWithThrottleRetryAsync(appKey, appSecret, page, cancellationToken);
            pageCount = ReadInt(doc, "pageCount") ?? 0;
            all.AddRange(ParseOrders(doc));
            page++;
        }
        while (page < pageCount && page < MaxPageLoops);

        return all;
    }

    // ── Parse (testlenebilir saf statik) ─────────────────────────────────────────────────────────────

    /// <summary>DetailedOrderListResponse'u kanal-agnostik <see cref="RemoteOrder"/>'lara çevirir (namespace/sıra
    /// agnostik). Kalem-merkezli model order düzeyine düzleşir: order-kargo = İLK kalemin shipmentInfo'su; satır
    /// tutarı <c>price</c> (kalem toplamı), birim fiyat = price/quantity (miktar 0 ise price). Alan adları WSDL'den.</summary>
    public static List<RemoteOrder> ParseOrders(XDocument doc)
    {
        var result = new List<RemoteOrder>();
        foreach (var order in doc.Descendants().Where(e => e.Name.LocalName == "order"))
        {
            var lines = ParseLines(order);
            var firstShipment = FirstShipmentInfo(order);

            result.Add(new RemoteOrder(
                RemoteOrderId: Local(order, "id") ?? Local(order, "orderNumber") ?? string.Empty,
                OrderNumber: Local(order, "orderNumber") ?? Local(order, "id") ?? string.Empty,
                OrderDate: ParseN11DateTimeUtc(Local(order, "createDate")) ?? DateTime.UtcNow,
                RemoteStatus: NullIfEmpty(Local(order, "status")),
                CustomerName: NullIfEmpty(Local(FindChild(order, "buyer"), "fullName")),
                TotalAmount: ParseDecimal(Local(order, "totalAmount")) ?? SumLines(lines),
                CargoProvider: NullIfEmpty(Local(FindChild(firstShipment, "shipmentCompany"), "name")),
                CargoTrackingNumber: NullIfEmpty(Local(firstShipment, "trackingNumber")),
                Lines: lines));
        }

        return result;
    }

    private static List<RemoteOrderLine> ParseLines(XElement order)
    {
        var lines = new List<RemoteOrderLine>();
        var itemList = FindChild(order, "orderItemList");
        if (itemList is null)
        {
            return lines;
        }

        foreach (var item in itemList.Elements().Where(e => e.Name.LocalName == "orderItem" || e.Name.LocalName == "item"))
        {
            var quantity = ParseDecimal(Local(item, "quantity")) ?? 0m;
            var price = ParseDecimal(Local(item, "price")) ?? 0m;

            lines.Add(new RemoteOrderLine(
                RemoteLineId: NullIfEmpty(Local(item, "id")),
                Barcode: null,
                StockCode: NullIfEmpty(Local(item, "productSellerCode")),
                ProductName: Local(item, "productName") ?? string.Empty,
                Quantity: quantity,
                UnitPrice: quantity > 0m ? price / quantity : price,
                LineTotal: price,
                RemoteLineStatus: NullIfEmpty(Local(item, "status"))));
        }

        return lines;
    }

    private static XElement? FirstShipmentInfo(XElement order)
    {
        var itemList = FindChild(order, "orderItemList");
        var firstItem = itemList?.Elements().FirstOrDefault(e => e.Name.LocalName == "orderItem" || e.Name.LocalName == "item");
        return FindChild(firstItem, "shipmentInfo");
    }

    private static decimal SumLines(IReadOnlyList<RemoteOrderLine> lines)
    {
        var sum = 0m;
        foreach (var line in lines)
        {
            sum += line.LineTotal;
        }

        return sum;
    }

    // ── SOAP gönderim + throttle retry ───────────────────────────────────────────────────────────────

    private async Task<XDocument> PostWithThrottleRetryAsync(
        string appKey, string appSecret, int page, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var doc = await PostAsync(appKey, appSecret, page, cancellationToken);
            var status = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "status")?.Value.Trim();
            if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return doc;
            }

            var message = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "errorMessage")?.Value.Trim();
            if (IsThrottle(message) && attempt < MaxThrottleRetries)
            {
                _logger.LogWarning("N11 sipariş ucu throttle ({Message}) — {Wait}s bekle (deneme {Attempt}).", message, ThrottleWaitSeconds, attempt);
                await Task.Delay(TimeSpan.FromSeconds(ThrottleWaitSeconds), cancellationToken);
                continue;
            }

            throw new BusinessException("TradeXpress:N11:Order:ListRejected").WithData("message", message ?? status ?? "unknown");
        }
    }

    // N11 throttle mesajı: "detailedOrders belli süre aralıklarıyla güncellenebilir".
    private static bool IsThrottle(string? message)
    {
        return message is not null && message.Contains("belli süre", StringComparison.OrdinalIgnoreCase);
    }

    // TARİH FİLTRESİ YOK (searchData boş) → N11 tüm sipariş geçmişini döndürür. period gönderilseydi eski siparişler
    // (test hesabında 2017) gizlenirdi — canlı doğrulandı (2026-07-11): period'suz totalCount=106, period'lu (son 40 gün)=0.
    private static async Task<XDocument> PostAsync(
        string appKey, string appSecret, int page, CancellationToken cancellationToken)
    {
        var request = new XElement(Sch + "DetailedOrderListRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            new XElement("auth", new XElement("appKey", appKey), new XElement("appSecret", appSecret)),
            new XElement("searchData"),
            new XElement("pagingData", new XElement("currentPage", page), new XElement("pageSize", PageSize)));

        var envelope = new XDocument(new XElement(Soapenv + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soapenv", Soapenv),
            new XElement(Soapenv + "Header"),
            new XElement(Soapenv + "Body", request)));

        using var content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        content.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };

        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException("TradeXpress:N11:Order:ListFailed").WithData("status", (int)response.StatusCode);
        }

        return XDocument.Parse(body);
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    // N11 createDate "dd/MM/yyyy HH:mm" (GMT+3) → UTC. Birden çok biçime toleranslı; çözülemezse null.
    private static DateTime? ParseN11DateTimeUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[] { "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy" };
        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return DateTime.SpecifyKind(local.AddHours(-3), DateTimeKind.Utc);
        }

        return null;
    }

    private static XElement? FindChild(XElement? parent, string localName)
    {
        return parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
    }

    private static string? Local(XElement? parent, string localName)
    {
        return parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();
    }

    private static int? ReadInt(XDocument doc, string localName)
    {
        var raw = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
