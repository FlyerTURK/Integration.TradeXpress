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

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// <see cref="IN11ShipmentTemplateClient"/> — N11 SOAP ShipmentService. CreateOrUpdate'i resmî örnek biçiminde
/// serialize eder (ShipmentApiModel alan sırası + adres city/district; N11 sıra-toleranslı); GetList'i namespace+sıra
/// agnostik parse eder (yanıt alfabetik döner). Prefix'li wrapper + unqualified children. Şartlı kargo push'ta
/// hem gönderir hem okur (feeCondition ByPrice/ByUnit). Sınıf adı arayüzle eşleştiğinden
/// ABP auto-expose. Sir loglanmaz.
/// </summary>
public sealed class N11ShipmentTemplateClient : IN11ShipmentTemplateClient, ITransientDependency
{
    private const string Endpoint = "https://api.n11.com/ws/ShipmentService.wsdl";
    private static readonly XNamespace Soapenv = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Sch = "http://www.n11.com/ws/schemas";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    // ── GetShipmentTemplateList (içe aktarım) ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<N11ShipmentTemplateData>> GetTemplateListAsync(string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var request = new XElement(Sch + "GetShipmentTemplateListRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            Auth(appKey, appSecret),
            new XElement("pagingData", new XElement("currentPage", 0), new XElement("pageSize", 100)));

        var response = await PostAsync(request, appKey, appSecret, cancellationToken);
        EnsureSuccess(response);

        return response.Descendants().Where(e => e.Name.LocalName == "shipmentTemplate")
            .Select(ParseTemplate)
            .ToList();
    }

    // ── CreateOrUpdateShipmentTemplate (push) ──────────────────────────────────────────────────────

    public async Task CreateOrUpdateAsync(N11ShipmentTemplateData template, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var request = new XElement(Sch + "CreateOrUpdateShipmentTemplateRequest",
            new XAttribute(XNamespace.Xmlns + "sch", Sch),
            Auth(appKey, appSecret),
            BuildShipment(template));

        var response = await PostAsync(request, appKey, appSecret, cancellationToken);
        EnsureSuccess(response);
    }

    // ── Serialize ──────────────────────────────────────────────────────────────────────────────────

    // ShipmentApiModel — resmî v4.6 örnek/alan sırası. Şartlı kargo adres elementine gömülü (BuildAddress → FeeConditionElements).
    private static XElement BuildShipment(N11ShipmentTemplateData t)
    {
        return new XElement("shipment",
            new XElement("templateName", t.TemplateName),
            new XElement("installmentInfo", t.InstallmentInfo ?? string.Empty),
            new XElement("exchangeInfo", t.ExchangeInfo ?? string.Empty),
            new XElement("shippingInfo", t.ShippingInfo ?? string.Empty),
            new XElement("specialDelivery", Bool(t.SpecialDelivery)),
            new XElement("deliveryFeeType", t.DeliveryFeeType.ToString(CultureInfo.InvariantCulture)),
            new XElement("combinedShipmentAllowed", Bool(t.CombinedShipmentAllowed)),
            new XElement("shipmentMethod", t.ShipmentMethod.ToString(CultureInfo.InvariantCulture)),
            BuildAddress("warehouseAddress", t.WarehouseAddress),
            t.ExchangeAddress is null ? null : BuildAddress("exchangeAddress", t.ExchangeAddress),
            new XElement("shipmentCompanies", t.ShipmentCompanies.Select(BuildCompany)),
            t.DeliverableCities.Count == 0 ? null : new XElement("deliverableCities", t.DeliverableCities.Select(BuildCity)),
            t.ClaimShipmentCompany is null ? null : BuildCompany(t.ClaimShipmentCompany, "claimShipmentCompany"),
            Optional("cargoAccountNo", t.CargoAccountNo),
            new XElement("useDmallCargo", Bool(t.UseDmallCargo)));
    }

    // ShipmentSaveAddress — resmî örnek sırası: title, address, city{code,name}, district{id,name}, postalCode + şartlı kargo.
    private static XElement BuildAddress(string elementName, N11ShipmentAddressData a)
    {
        return new XElement(elementName,
            Optional("title", a.Title),
            new XElement("address", a.Line),
            new XElement("city", new XElement("code", a.CityCode), new XElement("name", a.CityName)),
            string.IsNullOrWhiteSpace(a.DistrictId)
                ? null
                : new XElement("district", new XElement("id", a.DistrictId), new XElement("name", a.DistrictName ?? string.Empty)),
            Optional("postalCode", a.PostalCode),
            FeeConditionElements(a));
    }

    // Şartlı kargo (canlı doğrulandı — push kabul edilir): feeCondition tip 1=ByPrice→feeConditionPrice, 2=ByUnit→feeConditionUnit.
    private static IEnumerable<XElement> FeeConditionElements(N11ShipmentAddressData a)
    {
        if (a.ConditionalShippingThreshold is not { } threshold)
        {
            yield break;
        }

        var value = threshold.ToString(CultureInfo.InvariantCulture);
        if (a.ConditionalShippingUnit == N11ConditionalShippingUnit.Quantity)
        {
            yield return new XElement("feeCondition", "2");
            yield return new XElement("feeConditionUnit", value);
        }
        else
        {
            yield return new XElement("feeCondition", "1");
            yield return new XElement("feeConditionPrice", value);
        }
    }

    private static XElement BuildCompany(N11ShipmentCompanyRef c)
    {
        return BuildCompany(c, "shipmentCompany");
    }

    private static XElement BuildCompany(N11ShipmentCompanyRef c, string elementName)
    {
        return new XElement(elementName, new XElement("name", c.Name), new XElement("shortName", c.ShortName));
    }

    private static XElement BuildCity(N11ShipmentCityRef c)
    {
        return new XElement("city", new XElement("code", c.Code), new XElement("name", c.Name));
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

    // ── Parse (GetList → data) ─────────────────────────────────────────────────────────────────────

    private static N11ShipmentTemplateData ParseTemplate(XElement t)
    {
        var companies = t.Elements().FirstOrDefault(e => e.Name.LocalName == "shipmentCompanies")
            ?.Elements().Where(e => e.Name.LocalName == "shipmentCompany").Select(ParseCompany).ToList()
            ?? new List<N11ShipmentCompanyRef>();

        var cities = t.Elements().FirstOrDefault(e => e.Name.LocalName == "deliverableCities")
            ?.Elements().Where(e => e.Name.LocalName == "city").Select(ParseCity).ToList()
            ?? new List<N11ShipmentCityRef>();

        var claim = t.Elements().FirstOrDefault(e => e.Name.LocalName == "claimShipmentCompany");

        return new N11ShipmentTemplateData(
            Local(t, "templateName") ?? string.Empty,
            ParseByte(Local(t, "deliveryFeeType")),
            ParseByte(Local(t, "shipmentMethod")),
            ParseBool(Local(t, "specialDelivery")),
            ParseBool(Local(t, "combinedShipmentAllowed")),
            ParseBool(Local(t, "useDmallCargo")),
            NullIfEmpty(Local(t, "shippingInfo")),
            NullIfEmpty(Local(t, "exchangeInfo")),
            NullIfEmpty(Local(t, "installmentInfo")),
            NullIfEmpty(Local(t, "cargoAccountNo")),
            claim is null ? null : ParseCompany(claim),
            ParseAddress(t.Elements().FirstOrDefault(e => e.Name.LocalName == "warehouseAddress")),
            ParseAddressOrNull(t.Elements().FirstOrDefault(e => e.Name.LocalName == "exchangeAddress")),
            companies,
            cities);
    }

    private static N11ShipmentAddressData ParseAddress(XElement? a)
    {
        if (a is null)
        {
            return new N11ShipmentAddressData(null, string.Empty, string.Empty, string.Empty, null, null, null);
        }

        var district = a.Elements().FirstOrDefault(e => e.Name.LocalName == "district");
        var city = a.Elements().FirstOrDefault(e => e.Name.LocalName == "city");
        var (threshold, unit) = ParseConditionalShipping(a);

        return new N11ShipmentAddressData(
            NullIfEmpty(Local(a, "title")),
            Local(a, "address") ?? string.Empty,
            city is null ? string.Empty : Local(city, "code") ?? string.Empty,
            city is null ? string.Empty : Local(city, "name") ?? string.Empty,
            district is null ? null : NullIfEmpty(Local(district, "id")),
            district is null ? null : NullIfEmpty(Local(district, "name")),
            NullIfEmpty(Local(a, "postalCode")),
            threshold,
            unit);
    }

    // Şartlı kargo (resmî v4.6): feeCondition tip — 1=ByPrice (değer feeConditionPrice), 2=ByUnit (değer feeConditionUnit).
    private static (decimal? Threshold, N11ConditionalShippingUnit? Unit) ParseConditionalShipping(XElement a)
    {
        return Local(a, "feeCondition") switch
        {
            "1" => (ParseDecimal(Local(a, "feeConditionPrice")), N11ConditionalShippingUnit.Amount),
            "2" => (ParseDecimal(Local(a, "feeConditionUnit")), N11ConditionalShippingUnit.Quantity),
            _ => (null, null),
        };
    }

    private static N11ShipmentAddressData? ParseAddressOrNull(XElement? a)
    {
        if (a is null || a.Elements().All(e => string.IsNullOrWhiteSpace(e.Value)))
        {
            return null;
        }

        return ParseAddress(a);
    }

    private static N11ShipmentCompanyRef ParseCompany(XElement c)
    {
        return new N11ShipmentCompanyRef(Local(c, "name") ?? string.Empty, Local(c, "shortName") ?? string.Empty);
    }

    private static N11ShipmentCityRef ParseCity(XElement c)
    {
        return new N11ShipmentCityRef(Local(c, "code") ?? string.Empty, Local(c, "name") ?? string.Empty);
    }

    // ── HTTP + yardımcılar ─────────────────────────────────────────────────────────────────────────

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
            throw new BusinessException("TradeXpress:N11:ShipmentTemplateFailed").WithData("status", (int)response.StatusCode);
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
        throw new BusinessException("TradeXpress:N11:ShipmentTemplateRejected").WithData("message", message ?? status ?? "unknown");
    }

    private static string? Local(XElement parent, string localName)
    {
        return parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim();
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool ParseBool(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
    }

    private static byte ParseByte(string? value)
    {
        return byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b) ? b : (byte)0;
    }

    private static decimal? ParseDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
