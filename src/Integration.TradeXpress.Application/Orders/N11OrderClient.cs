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
using Integration.TradeXpress.N11Products;
using Microsoft.Extensions.Options;
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
    private static readonly XNamespace Soapenv = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Sch = "http://www.n11.com/ws/schemas";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(40) };

    private const int PageSize = 100;
    private const int MaxThrottleRetries = 5;
    private const int ThrottleWaitSeconds = 6;

    private readonly ILogger<N11OrderClient> _logger;

    // Uç adresi N11EndpointOptions'tan gelir (varsayılan https://api.n11.com). Sabit adres, istekleri yerel
    // bir sahte sunucuya yönlendirmeyi imkânsız kılıyordu — hesap kapalıyken denemenin tek yolu bu.
    private readonly N11EndpointOptions _endpoints;

    private string Endpoint
    {
        get { return _endpoints.OrderServiceEndpoint; }
    }

    public N11OrderClient(ILogger<N11OrderClient> logger, IOptions<N11EndpointOptions> endpointOptions)
    {
        _logger = logger;
        _endpoints = endpointOptions.Value;
    }

    public async Task<N11OrdersPage> GetOrdersPageAsync(
        string appKey, string appSecret, int page, DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        var request = BuildListRequest(appKey, appSecret, page, sinceUtc);
        var doc = await PostWithThrottleRetryAsync(
            request, "TradeXpress:N11:Order:ListFailed", "TradeXpress:N11:Order:ListRejected", cancellationToken);
        var pageCount = ReadInt(doc, "pageCount") ?? 0;
        return new N11OrdersPage(ParseOrders(doc), pageCount);
    }

    public async Task<OrderDetailSnapshot?> GetOrderDetailAsync(
        string appKey, string appSecret, string n11OrderId, DateTime fetchedAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(n11OrderId))
        {
            return null;
        }

        var request = BuildDetailRequest(appKey, appSecret, n11OrderId);
        var doc = await PostWithThrottleRetryAsync(
            request, "TradeXpress:N11:Order:DetailFailed", "TradeXpress:N11:Order:DetailRejected", cancellationToken);
        return ParseOrderDetail(doc, fetchedAt);
    }

    // ── YAZMA uçları (Sipariş Fazı O2) — GERÇEK pazaryerine, geri alınamaz. Throttle-retry YOK (çift-aksiyon riski). ──

    public async Task AcceptOrderItemAsync(
        string appKey, string appSecret, IReadOnlyList<long> n11OrderItemIds, int numberOfPackages, CancellationToken cancellationToken = default)
    {
        var request = new XElement(Sch + "OrderItemAcceptRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            new XElement("auth", new XElement("appKey", appKey), new XElement("appSecret", appSecret)),
            new XElement("orderItemList", BuildOrderItemIdElements(n11OrderItemIds)),
            new XElement("numberOfPackages", numberOfPackages));
        await PostWriteEnvelopeAsync(request, "TradeXpress:N11:Order:AcceptFailed", "TradeXpress:N11:Order:AcceptRejected", cancellationToken);
    }

    public async Task RejectOrderItemAsync(
        string appKey, string appSecret, IReadOnlyList<long> n11OrderItemIds, string reason, CancellationToken cancellationToken = default)
    {
        var request = new XElement(Sch + "OrderItemRejectRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            new XElement("auth", new XElement("appKey", appKey), new XElement("appSecret", appSecret)),
            new XElement("orderItemList", BuildOrderItemIdElements(n11OrderItemIds)),
            new XElement("rejectReason", reason));
        await PostWriteEnvelopeAsync(request, "TradeXpress:N11:Order:RejectFailed", "TradeXpress:N11:Order:RejectRejected", cancellationToken);
    }

    private static IEnumerable<XElement> BuildOrderItemIdElements(IReadOnlyList<long> n11OrderItemIds)
    {
        return n11OrderItemIds.Select(id => new XElement("orderItem", new XElement("id", id)));
    }

    public async Task MakeShipmentAsync(
        string appKey, string appSecret, long n11OrderItemId, string shipmentCompanyId,
        string trackingNumber, string? campaignNumber, int shipmentMethod, CancellationToken cancellationToken = default)
    {
        var request = new XElement(Sch + "MakeOrderItemShipmentRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            new XElement("auth", new XElement("appKey", appKey), new XElement("appSecret", appSecret)),
            new XElement("orderItemList",
                new XElement("orderItem",
                    new XElement("id", n11OrderItemId),
                    new XElement("shipmentInfo",
                        new XElement("shipmentCompany", new XElement("id", shipmentCompanyId)),
                        new XElement("campaignNumber", campaignNumber ?? string.Empty),
                        new XElement("trackingNumber", trackingNumber),
                        new XElement("shipmentMethod", shipmentMethod)))));
        await PostWriteEnvelopeAsync(request, "TradeXpress:N11:Order:ShipmentFailed", "TradeXpress:N11:Order:ShipmentRejected", cancellationToken);
    }

    // ── Parse (testlenebilir saf statik) ─────────────────────────────────────────────────────────────

    /// <summary>DetailedOrderListResponse'u kanal-agnostik <see cref="RemoteOrder"/>'lara çevirir (namespace/sıra
    /// agnostik). Kalem-merkezli model order düzeyine düzleşir: order-kargo = İLK kalemin shipmentInfo'su; kalemdeki
    /// <c>price</c> BİRİM fiyattır, satır toplamı = price × quantity (miktar 0 ise price). Alan adları WSDL'den.</summary>
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

            // ⚠ N11'de <price> BİRİM fiyattır — satır toplamı DEĞİL. Eskiden tersi varsayılıyordu (birim fiyat
            // price/quantity, satır toplamı price) → adet>1 olan her kalemde ikisi de adet katı kadar yanlıştı ve
            // rezervasyon/PriceDifference bu sayıdan beslenecekti. Canlı ölçümle kanıtlandı (2026-08-07, 126 kalem):
            // N11'in gönderdiği başlık totalAmount'ı Σ(price × quantity)'e kuruşu kuruşuna eşit.
            var unitPrice = ParseDecimal(Local(item, "price")) ?? 0m;

            lines.Add(new RemoteOrderLine(
                RemoteLineId: NullIfEmpty(Local(item, "id")),
                Barcode: null,
                StockCode: NullIfEmpty(Local(item, "productSellerCode")),
                ProductName: Local(item, "productName") ?? string.Empty,
                Quantity: quantity,
                UnitPrice: unitPrice,
                // Adet 0 savunma kolu: kalem KAYBEDİLMEZ, toplam birim fiyata düşer (mevcut fail-open korunur).
                LineTotal: quantity > 0m ? unitPrice * quantity : unitPrice,
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

    // ── getOrderDetail parse (testlenebilir saf statik) → zengin snapshot VO ───────────────────────────

    /// <summary>OrderDetailResponse'u kanal-agnostik <see cref="OrderDetailSnapshot"/>'a çevirir (namespace/sıra
    /// agnostik). Alıcı + fatura/teslimat adresi + billingTemplate tutar kırılımı + itemList (komisyon/indirim/kargo/
    /// nitelik). Alan yolları N11 SOAP ref v4.6'dan. Boş/eksik alanlar null (tolerant snapshot).</summary>
    public static OrderDetailSnapshot? ParseOrderDetail(XDocument doc, DateTime fetchedAt)
    {
        var detail = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "orderDetail");
        if (detail is null)
        {
            return null;
        }

        var buyerEl = FindChild(detail, "buyer");
        var buyer = new OrderDetailParty(
            Local(buyerEl, "fullName"), Local(buyerEl, "email"), Local(buyerEl, "tcId"),
            Local(buyerEl, "taxId"), Local(buyerEl, "taxOffice"));

        var template = FindChild(detail, "billingTemplate");
        var totals = template is null ? null : new OrderDetailTotals(
            ParseDecimal(Local(template, "originalPrice")),
            ParseDecimal(Local(template, "dueAmount")),
            ParseDecimal(Local(template, "sellerInvoiceAmount")),
            ParseDecimal(Local(template, "totalMallDiscountPrice")),
            ParseDecimal(Local(template, "totalSellerDiscount")),
            ParseDecimal(Local(template, "totalServiceItemOriginalPrice")));

        return new OrderDetailSnapshot(
            buyer,
            ParseAddress(FindChild(detail, "billingAddress")),
            ParseAddress(FindChild(detail, "shippingAddress")),
            ParseInt(Local(detail, "invoiceType")),
            NullIfEmpty(Local(detail, "paymentType")),
            NullIfEmpty(Local(detail, "citizenshipId")),
            totals,
            ParseDetailItems(detail),
            fetchedAt);
    }

    private static OrderDetailAddress? ParseAddress(XElement? el)
    {
        if (el is null)
        {
            return null;
        }

        // N11 CANLI yanıtı (2026-07-24 doğrulandı, order 136043971): ADRES elementlerinde vergi dairesi alan adı
        // "taxHouse" ('taxOffice' DEĞİL — o yalnız buyer elementinde). Yanlış adla kurumsal faturalı siparişlerde
        // adres vergi dairesi sessizce hep null'du (approvedDate/shippingDate wire-ad hatasının ikizi).
        // Emniyet: eski/dokümante ad da fallback olarak okunur (yanıt varyasyonuna tolerans).
        var address = new OrderDetailAddress(
            Local(el, "fullName"), Local(el, "address"), Local(el, "neighborhood"), Local(el, "district"),
            Local(el, "city"), Local(el, "postalCode"), Local(el, "gsm"), Local(el, "tcId"),
            Local(el, "taxId"), Local(el, "taxHouse") ?? Local(el, "taxOffice"));
        return address.HasAny() ? address : null;
    }

    private static List<OrderDetailItem> ParseDetailItems(XElement detail)
    {
        var items = new List<OrderDetailItem>();
        var itemList = FindChild(detail, "itemList");
        if (itemList is null)
        {
            return items;
        }

        foreach (var item in itemList.Elements().Where(e => e.Name.LocalName == "item" || e.Name.LocalName == "orderItem"))
        {
            var shipmentInfo = FindChild(item, "shipmentInfo");
            items.Add(new OrderDetailItem(
                remoteLineId: NullIfEmpty(Local(item, "id")),
                productId: NullIfEmpty(Local(item, "productId")),
                productName: Local(item, "productName"),
                productSellerCode: NullIfEmpty(Local(item, "productSellerCode")),
                skuId: NullIfEmpty(Local(item, "stockKeepingUnitId")),
                quantity: ParseDecimal(Local(item, "quantity")) ?? 0m,
                price: ParseDecimal(Local(item, "price")) ?? 0m,
                commission: ParseDecimal(Local(item, "commission")),
                dueAmount: ParseDecimal(Local(item, "dueAmount")),
                mallDiscount: ParseDecimal(Local(item, "mallDiscount")),
                sellerDiscount: ParseDecimal(Local(item, "sellerDiscount")),
                sellerInvoiceAmount: ParseDecimal(Local(item, "sellerInvoiceAmount")),
                status: NullIfEmpty(Local(item, "status")),
                // N11 CANLI yanıtı (2026-07-11 doğrulandı, order 136043971): alan adları DOC'tan FARKLI — "approvedDate"
                // ('d' ile, doc "approveDate" YANLIŞ) + "shippingDate" (doc "shipmentDate" YANLIŞ). Yanlış adla 0/252 null'du.
                approveDate: ParseN11DateTimeUtc(Local(item, "approvedDate")),
                shipmentDate: ParseN11DateTimeUtc(Local(item, "shippingDate")),
                shipmentCompany: NullIfEmpty(Local(FindChild(shipmentInfo, "shipmentCompany"), "name")),
                shipmentMethod: ParseInt(Local(shipmentInfo, "shipmentMethod")),
                shipmentCode: NullIfEmpty(Local(shipmentInfo, "shipmentCode")),
                shipmentCompanyId: NullIfEmpty(Local(FindChild(shipmentInfo, "shipmentCompany"), "id")),
                shipmentCompanyShortName: NullIfEmpty(Local(FindChild(shipmentInfo, "shipmentCompany"), "shortName")),
                trackingNumber: NullIfEmpty(Local(shipmentInfo, "trackingNumber")),
                campaignNumber: NullIfEmpty(Local(shipmentInfo, "campaignNumber")),
                campaignNumberStatus: NullIfEmpty(Local(shipmentInfo, "campaignNumberStatus")),
                attributes: ParseItemAttributes(item),
                customTexts: ParseCustomTexts(item)));
        }

        return items;
    }

    private static List<OrderDetailItemAttribute> ParseItemAttributes(XElement item)
    {
        var result = new List<OrderDetailItemAttribute>();
        var attributes = FindChild(item, "attributes");
        if (attributes is null)
        {
            return result;
        }

        foreach (var attr in attributes.Elements().Where(e => e.Name.LocalName == "attribute"))
        {
            var name = NullIfEmpty(Local(attr, "name"));
            var value = NullIfEmpty(Local(attr, "value"));
            if (name is not null || value is not null)
            {
                result.Add(new OrderDetailItemAttribute(name, value));
            }
        }

        return result;
    }

    // Alıcının girdiği özel metinler (customTextOptionValues.customTextOptionValue → option/text). Kişiselleştirilmiş
    // üründe ne yazılacağı (kaşe/mühür metni, mürekkep rengi). text çok satırlı olabilir (adres) — Value korunur.
    private static List<OrderDetailItemCustomText> ParseCustomTexts(XElement item)
    {
        var result = new List<OrderDetailItemCustomText>();
        var container = FindChild(item, "customTextOptionValues");
        if (container is null)
        {
            return result;
        }

        foreach (var v in container.Elements().Where(e => e.Name.LocalName == "customTextOptionValue"))
        {
            var option = NullIfEmpty(Local(v, "option"));
            var text = NullIfEmpty(Local(v, "text"));
            if (option is not null || text is not null)
            {
                result.Add(new OrderDetailItemCustomText(option, text));
            }
        }

        return result;
    }

    // ── SOAP gönderim + throttle retry ───────────────────────────────────────────────────────────────

    // Ortak SOAP retry döngüsü (liste + detay paylaşır): success → döner; throttle → bekle+tekrar; aksi → reject hatası.
    private async Task<XDocument> PostWithThrottleRetryAsync(
        XElement requestBody, string transportErrorCode, string rejectErrorCode, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var doc = await PostEnvelopeAsync(requestBody, transportErrorCode, cancellationToken);
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

            throw new BusinessException(rejectErrorCode).WithData("message", message ?? status ?? "unknown");
        }
    }

    // N11 throttle mesajı: "detailedOrders belli süre aralıklarıyla güncellenebilir".
    private static bool IsThrottle(string? message)
    {
        return message is not null && message.Contains("belli süre", StringComparison.OrdinalIgnoreCase);
    }

    // YAZMA uçları için: TEK deneme (retry YOK — throttle'da tekrar denemek çift-aksiyon riski taşır; GERÇEK
    // pazaryerine yazan bir çağrının belirsiz sonucunda kör tekrar yapmak yerine hata dostane fırlatılır).
    private async Task PostWriteEnvelopeAsync(
        XElement requestBody, string transportErrorCode, string rejectErrorCode, CancellationToken cancellationToken)
    {
        var doc = await PostEnvelopeAsync(requestBody, transportErrorCode, cancellationToken);
        var status = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "status")?.Value.Trim();
        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var message = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "errorMessage")?.Value.Trim();
        throw new BusinessException(rejectErrorCode).WithData("message", message ?? status ?? "unknown");
    }

    // İKİ AYRI STRATEJİ — biri diğerinin yerine geçmez:
    //
    //  ① SEED (sinceUtc = null): searchData BOŞ → N11 tüm sipariş geçmişini döndürür. Canlı doğrulandı
    //     (2026-07-11): period'suz totalCount=106, period'lu (son 40 gün)=0. Yani period göndermek boş kanalın
    //     geçmişini GİZLERDİ; ilk kurulum bu yüzden filtresizdir.
    //  ② DELTA (sinceUtc dolu): dar pencere. Dolu kanalı 2 dakikada bir tüm geçmişiyle taramak throttle
    //     bütçesini yakar ve her turda aynı 106 siparişi yeniden yazardı.
    //
    // ⚠ DELTA'NIN KÖR NOKTASI: period filtresi sipariş TARİHİNE bakar, statü DEĞİŞİMİNE değil. Pencere dışında
    // kalan eski bir siparişin iptali bu listeye HİÇ düşmez — o yüzden delta kolu ayrıca açık siparişlerin
    // detayını tazeler (OrderSyncManager). Buradaki filtre tek başına yeterli DEĞİLDİR.
    private static XElement BuildListRequest(string appKey, string appSecret, int page, DateTime? sinceUtc)
    {
        var searchData = new XElement("searchData");
        if (sinceUtc is { } since)
        {
            // N11 tarih biçimi dd/MM/yyyy. Bitiş açık uçlu bırakılmaz (N11 ikisini birlikte ister);
            // "bugün + 1 gün" saat dilimi farkında bugünün siparişlerini dışarıda bırakmayı önler.
            searchData.Add(new XElement("period",
                new XElement("startDate", since.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                new XElement("endDate", DateTime.UtcNow.AddDays(1).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))));
        }

        return new XElement(Sch + "DetailedOrderListRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            new XElement("auth", new XElement("appKey", appKey), new XElement("appSecret", appSecret)),
            searchData,
            new XElement("pagingData", new XElement("currentPage", page), new XElement("pageSize", PageSize)));
    }

    // getOrderDetail isteği (SOAP ref v4.6): auth + orderRequest.id (N11 sipariş id).
    private static XElement BuildDetailRequest(string appKey, string appSecret, string n11OrderId)
    {
        return new XElement(Sch + "OrderDetailRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            new XElement("auth", new XElement("appKey", appKey), new XElement("appSecret", appSecret)),
            new XElement("orderRequest", new XElement("id", n11OrderId)));
    }

    private async Task<XDocument> PostEnvelopeAsync(
        XElement requestBody, string transportErrorCode, CancellationToken cancellationToken)
    {
        var envelope = new XDocument(new XElement(Soapenv + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soapenv", Soapenv),
            new XElement(Soapenv + "Header"),
            new XElement(Soapenv + "Body", requestBody)));

        using var content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        content.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };

        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(transportErrorCode).WithData("status", (int)response.StatusCode);
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

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
