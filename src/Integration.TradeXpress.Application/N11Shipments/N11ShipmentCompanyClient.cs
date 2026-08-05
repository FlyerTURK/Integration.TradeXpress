using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Volo.Abp;
using Integration.TradeXpress.N11Products;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// <see cref="IN11ShipmentCompanyClient"/> — N11 SOAP ShipmentCompanyService.GetShipmentCompanies (~68 firma).
/// Auth SOAP gövdesinde + header hedge. Namespace-agnostik parse. Sınıf adı arayüzle eşleştiğinden ABP auto-expose.
/// </summary>
public sealed class N11ShipmentCompanyClient : IN11ShipmentCompanyClient, ITransientDependency
{
    // Uc adresi N11EndpointOptions'tan gelir (varsayilan https://api.n11.com). Sabit adres, istekleri
    // yerel bir sahte sunucuya yonlendirmeyi imkansiz kiliyordu — hesap kapaliyken denemenin tek yolu bu.
    private readonly N11EndpointOptions _endpoints;

    private string Endpoint
    {
        get { return _endpoints.ShipmentCompanyServiceEndpoint; }
    }

    public N11ShipmentCompanyClient(IOptions<N11EndpointOptions> endpointOptions)
    {
        _endpoints = endpointOptions.Value;
    }

    private const string SchemaNs = "http://www.n11.com/ws/schemas";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<IReadOnlyList<N11ShipmentCompanyRecord>> GetShipmentCompaniesAsync(string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var envelope =
            $"<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:sch=\"{SchemaNs}\">" +
            "<soapenv:Header/><soapenv:Body><sch:GetShipmentCompaniesRequest>" +
            $"<auth><appKey>{appKey}</appKey><appSecret>{appSecret}</appSecret></auth>" +
            "</sch:GetShipmentCompaniesRequest></soapenv:Body></soapenv:Envelope>";

        using var content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        content.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
        request.Headers.TryAddWithoutValidation("appkey", appKey);
        request.Headers.TryAddWithoutValidation("appsecret", appSecret);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException("TradeXpress:N11:ShipmentFetchFailed").WithData("status", (int)response.StatusCode);
        }

        var doc = XDocument.Parse(body);
        return doc.Descendants().Where(e => e.Name.LocalName == "shipmentCompany")
            .Select(c => new N11ShipmentCompanyRecord(Local(c, "id"), Local(c, "name"), Local(c, "shortName")))
            .Where(c => c.ExternalId.Length > 0)
            .ToList();
    }

    private static string Local(XElement parent, string localName)
    {
        var child = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
        return child?.Value.Trim() ?? string.Empty;
    }
}
