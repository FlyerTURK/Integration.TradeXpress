using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// <see cref="IN11ProductClient"/> — N11 SOAP ProductService.SaveProduct. ProductRequest'i WSDL xs:sequence sırasında
/// serialize eder; yanıtı namespace-agnostik parse eder (result/status + product.id/durumlar). Prefix'li wrapper +
/// unqualified children (kanıtlanmış N11 deseni). Sınıf adı arayüzle eşleştiğinden ABP auto-expose. Sir loglanmaz.
/// </summary>
public sealed class N11ProductClient : IN11ProductClient, ITransientDependency
{
    private const string Endpoint = "https://api.n11.com/ws/ProductService.wsdl";
    private static readonly XNamespace Soapenv = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Sch = "http://www.n11.com/ws/schemas";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<N11SaveProductResult> SaveProductAsync(N11ProductData product, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var request = new XElement(Sch + "SaveProductRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            Auth(appKey, appSecret),
            BuildProduct(product));

        var response = await PostAsync(request, appKey, appSecret, cancellationToken);
        EnsureSuccess(response);
        return ParseResult(response);
    }

    public async Task<N11ProductDetail> GetProductAsync(long n11ProductId, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var request = new XElement(Sch + "GetProductByProductIdRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            Auth(appKey, appSecret),
            new XElement("productId", n11ProductId.ToString(CultureInfo.InvariantCulture)));

        var response = await PostAsync(request, appKey, appSecret, cancellationToken);
        EnsureSuccess(response);
        return ParseDetail(response, n11ProductId);
    }

    public async Task<N11ProductDetail> GetProductBySellerCodeAsync(string sellerCode, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var request = new XElement(Sch + "GetProductBySellerCodeRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            Auth(appKey, appSecret),
            new XElement("sellerCode", sellerCode));

        var response = await PostAsync(request, appKey, appSecret, cancellationToken);
        EnsureSuccess(response);

        // Yanıttaki ürün id'si (varsa) parse edilir; yoksa 0 → çağıran N11ProductId'yi zaten biliyor.
        var product = response.Descendants().FirstOrDefault(e => e.Name.LocalName == "product");
        long n11Id = long.TryParse(product is null ? null : Local(product, "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
        return ParseDetail(response, n11Id);
    }

    public async Task<N11SaveProductResult> UpdateProductBasicAsync(N11ProductBasicUpdate update, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var request = new XElement(Sch + "UpdateProductBasicRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            Auth(appKey, appSecret),
            new XElement("productId", update.N11ProductId.ToString(CultureInfo.InvariantCulture)),
            new XElement("productSellerCode", update.ProductSellerCode),
            update.Price is { } price ? new XElement("price", price.ToString(CultureInfo.InvariantCulture)) : null,
            // İndirim ZORUNLU (WSDL); ürün-seviyesi indirimden beslenir (Type=0 = indirimsiz).
            new XElement("productDiscount",
                new XElement("discountType", update.Discount.Type.ToString(CultureInfo.InvariantCulture)),
                new XElement("discountValue", update.Discount.Value.ToString(CultureInfo.InvariantCulture)),
                new XElement("discountStartDate", update.Discount.StartDate),
                new XElement("discountEndDate", update.Discount.EndDate)),
            new XElement("stockItems", update.StockItems.Select(BuildBasicStockItem)),
            new XElement("description", update.Description));

        var response = await PostAsync(request, appKey, appSecret, cancellationToken);
        EnsureSuccess(response);
        return ParseResult(response);
    }

    private static XElement BuildBasicStockItem(N11ProductBasicStockItem s)
    {
        return new XElement("stockItem",
            new XElement("sellerStockCode", s.SellerStockCode),
            new XElement("id", s.N11SkuId.ToString(CultureInfo.InvariantCulture)),
            s.OptionPrice is { } op ? new XElement("optionPrice", op.ToString(CultureInfo.InvariantCulture)) : null,
            s.Quantity is { } q ? new XElement("quantity", q.ToString(CultureInfo.InvariantCulture)) : null);
    }

    // ── Serialize (ProductRequest — WSDL xs:sequence sırası) ────────────────────────────────────────

    private static XElement BuildProduct(N11ProductData p)
    {
        return new XElement("product",
            new XElement("productSellerCode", p.ProductSellerCode),
            new XElement("title", p.Title),
            new XElement("description", p.Description),
            new XElement("domestic", Bool(p.Domestic)),
            new XElement("category", new XElement("id", p.CategoryId)),
            p.SpecialInfo.Count == 0
                ? null
                : new XElement("specialProductInfoList",
                    p.SpecialInfo.Select(s => new XElement("specialProductInfo",
                        new XElement("key", s.Key),
                        new XElement("value", s.Value)))),
            new XElement("price", p.Price.ToString(CultureInfo.InvariantCulture)),
            new XElement("currencyType", p.CurrencyType.ToString(CultureInfo.InvariantCulture)),
            new XElement("images", p.Images.Select(i => new XElement("image",
                new XElement("url", i.Url),
                new XElement("order", i.Order.ToString(CultureInfo.InvariantCulture))))),
            new XElement("attributes", p.Attributes.Select(BuildAttribute)),
            // WSDL sırası: attributes → productionDate → expirationDate → productCondition. Boşsa gönderilmez.
            Optional("productionDate", p.ProductionDate),
            Optional("expirationDate", p.ExpirationDate),
            new XElement("productCondition", p.ProductCondition.ToString(CultureInfo.InvariantCulture)),
            new XElement("preparingDay", p.PreparingDay.ToString(CultureInfo.InvariantCulture)),
            // WSDL ProductRequest sırası: preparingDay → discount → shipmentTemplate. İndirim yoksa gönderilmez.
            p.Discount is { } d
                ? new XElement("discount",
                    new XElement("startDate", d.StartDate),
                    new XElement("endDate", d.EndDate),
                    new XElement("type", d.Type),
                    new XElement("value", d.Value))
                : null,
            new XElement("shipmentTemplate", p.ShipmentTemplate),
            new XElement("stockItems", p.StockItems.Select(BuildStockItem)),
            p.MaxPurchaseQuantity is { } mpq
                ? new XElement("maxPurchaseQuantity", mpq.ToString(CultureInfo.InvariantCulture))
                : null,
            // WSDL sırası sonu: ...maxPurchaseQuantity → sellerNote. Boşsa gönderilmez.
            Optional("sellerNote", p.SellerNote));
    }

    private static XElement BuildAttribute(N11ProductAttributePair a)
    {
        return new XElement("attribute", new XElement("name", a.Name), new XElement("value", a.Value));
    }

    // Element sırası WSDL ProductSkuRequest xs:sequence'ine UYAR: bundle?→mpn?→gtin?→n11CatalogId→oem?→quantity→
    // sellerStockCode→attributes→optionPrice→images (mpn/gtin quantity'den ÖNCE — Faz 1 sıra düzeltmesi; bugün
    // null oldukları için fark yok, dolduruldukları gün şema reddi riski sıfırlanır).
    private static XElement BuildStockItem(N11ProductStockItem s)
    {
        return new XElement("stockItem",
            Optional("mpn", s.Mpn),
            Optional("gtin", s.Gtin),
            Optional("oem", s.Oem),
            new XElement("quantity", s.Quantity.ToString(CultureInfo.InvariantCulture)),
            new XElement("sellerStockCode", s.SellerStockCode),
            s.Attributes.Count == 0 ? null : new XElement("attributes", s.Attributes.Select(BuildAttribute)),
            s.OptionPrice is { } op ? new XElement("optionPrice", op.ToString(CultureInfo.InvariantCulture)) : null);
    }

    private static XElement Auth(string appKey, string appSecret)
    {
        return new XElement("auth", new XElement("appKey", appKey), new XElement("appSecret", appSecret));
    }

    private static XElement? Optional(string name, string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : new XElement(name, value);
    }

    private static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    // ── Parse ───────────────────────────────────────────────────────────────────────────────────────

    private static N11SaveProductResult ParseResult(XDocument doc)
    {
        var product = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "product");
        if (product is null)
        {
            return new N11SaveProductResult(null, null, null, null, Array.Empty<N11SkuIdentity>());
        }

        long? n11Id = long.TryParse(Local(product, "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
        return new N11SaveProductResult(
            n11Id,
            NullIfEmpty(Local(product, "productSellerCode")),
            NullIfEmpty(Local(product, "saleStatus")),
            NullIfEmpty(Local(product, "approvalStatus")),
            ParseSkus(product));
    }

    /// <summary>Yanıttaki stockItems bloğundan SKU kimliklerini çıkarır (id/version — SKU-düzeyi mutabakat).
    /// Blok yoksa/boşsa boş liste; sellerStockCode'suz satır atlanır (eşlenemez).</summary>
    private static IReadOnlyList<N11SkuIdentity> ParseSkus(XElement product)
    {
        var wrapper = product.Elements().FirstOrDefault(e => e.Name.LocalName == "stockItems");
        if (wrapper is null)
        {
            return Array.Empty<N11SkuIdentity>();
        }

        return wrapper.Elements()
            .Where(e => e.Name.LocalName == "stockItem")
            .Select(s => new N11SkuIdentity(
                NullIfEmpty(Local(s, "sellerStockCode")) ?? string.Empty,
                long.TryParse(Local(s, "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var skuId) ? skuId : null,
                long.TryParse(Local(s, "version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version) ? version : null))
            .Where(s => s.SellerStockCode.Length > 0)
            .ToList();
    }

    /// <summary>GetProductByProductId yanıtındaki product'ı N11ProductDetail'e çevirir — alan yanıtta yoksa null
    /// (çağıran dokunmaz). Kategori adı fullName (tam yol) tercih edilir; attributes bloğu yoksa null.</summary>
    private static N11ProductDetail ParseDetail(XDocument doc, long n11ProductId)
    {
        var product = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "product");
        if (product is null)
        {
            return new N11ProductDetail(n11ProductId, null, null, null, null, null, null, null, null, null, null, Array.Empty<N11SkuIdentity>());
        }

        var category = product.Elements().FirstOrDefault(e => e.Name.LocalName == "category");
        var attributesElement = product.Elements().FirstOrDefault(e => e.Name.LocalName == "attributes");
        var attributes = attributesElement?
            .Elements().Where(e => e.Name.LocalName == "attribute")
            .Select(a => new N11ProductAttributePair(Local(a, "name") ?? string.Empty, Local(a, "value") ?? string.Empty))
            .Where(a => a.Name.Length > 0)
            .ToList();

        return new N11ProductDetail(
            n11ProductId,
            NullIfEmpty(Local(product, "title")),
            category is null ? null : NullIfEmpty(Local(category, "id")),
            category is null ? null : NullIfEmpty(Local(category, "fullName")) ?? NullIfEmpty(Local(category, "name")),
            NullIfEmpty(Local(product, "shipmentTemplate")),
            byte.TryParse(Local(product, "productCondition"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var condition) ? condition : null,
            int.TryParse(Local(product, "preparingDay"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var preparingDay) ? preparingDay : null,
            int.TryParse(Local(product, "maxPurchaseQuantity"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxPurchase) ? maxPurchase : null,
            NullIfEmpty(Local(product, "saleStatus")),
            NullIfEmpty(Local(product, "approvalStatus")),
            attributes,
            ParseSkus(product));
    }

    // ── HTTP + yardımcılar ──────────────────────────────────────────────────────────────────────────

    private static async Task<XDocument> PostAsync(XElement request, string appKey, string appSecret, CancellationToken cancellationToken)
    {
        var envelope = new XDocument(new XElement(Soapenv + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soapenv", Soapenv),
            new XElement(Soapenv + "Header"),
            new XElement(Soapenv + "Body", request)));

        using var content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        content.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
        httpRequest.Headers.TryAddWithoutValidation("appkey", appKey);
        httpRequest.Headers.TryAddWithoutValidation("appsecret", appSecret);

        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException("TradeXpress:N11:Product:SaveFailed").WithData("status", (int)response.StatusCode);
        }

        return XDocument.Parse(body);
    }

    // result/status = failure → errorMessage'ı taşıyan BusinessException.
    private static void EnsureSuccess(XDocument doc)
    {
        var status = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "status")?.Value.Trim();
        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var message = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "errorMessage")?.Value.Trim();
        throw new BusinessException("TradeXpress:N11:Product:SaveRejected").WithData("message", message ?? status ?? "unknown");
    }

    private static string? Local(XElement parent, string localName)
    {
        return parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
