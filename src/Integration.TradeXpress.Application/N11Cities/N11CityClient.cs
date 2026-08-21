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

namespace Integration.TradeXpress.N11Cities;

/// <summary>
/// <see cref="IN11CityClient"/> — N11 SOAP CityService (GetCities / GetDistrict(cityCode) / GetNeighborhoods(districtId)).
/// Auth SOAP body'sinde <c>&lt;auth&gt;</c> (+ header hedge). Namespace-agnostik (LocalName) parse. Sınıf adı arayüzle
/// eşleştiği için ABP otomatik expose eder. Sir ASLA loglanmaz.
/// </summary>
public sealed class N11CityClient : IN11CityClient, ITransientDependency
{
    // Uc adresi N11EndpointOptions'tan gelir (varsayilan https://api.n11.com). Sabit adres, istekleri
    // yerel bir sahte sunucuya yonlendirmeyi imkansiz kiliyordu — hesap kapaliyken denemenin tek yolu bu.
    private readonly N11EndpointOptions _endpoints;

    private string Endpoint
    {
        get { return _endpoints.CityServiceEndpoint; }
    }

    public N11CityClient(IOptions<N11EndpointOptions> endpointOptions)
    {
        _endpoints = endpointOptions.Value;
    }

    private const string SchemaNs = "http://www.n11.com/ws/schemas";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<IReadOnlyList<N11CityRecord>> GetCitiesAsync(string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var doc = await SoapAsync("GetCities", string.Empty, appKey, appSecret, cancellationToken);
        return doc.Descendants().Where(e => e.Name.LocalName == "city")
            .Select(c => new N11CityRecord(Local(c, "cityCode"), Local(c, "cityId"), Local(c, "cityName")))
            .Where(c => c.CityCode.Length > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<N11DistrictRecord>> GetDistrictsAsync(string cityCode, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var doc = await SoapAsync("GetDistrict", $"<cityCode>{cityCode}</cityCode>", appKey, appSecret, cancellationToken);
        return doc.Descendants().Where(e => e.Name.LocalName == "district")
            .Select(d => new N11DistrictRecord(Local(d, "id"), Local(d, "name")))
            .Where(d => d.DistrictId.Length > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<N11NeighborhoodRecord>> GetNeighborhoodsAsync(string districtId, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var doc = await SoapAsync("GetNeighborhoods", $"<districtId>{districtId}</districtId>", appKey, appSecret, cancellationToken);
        return doc.Descendants().Where(e => e.Name.LocalName == "neighborhood")
            .Select(n => new N11NeighborhoodRecord(Local(n, "id"), Local(n, "name")))
            .Where(n => n.Id.Length > 0)
            .ToList();
    }

    private async Task<XDocument> SoapAsync(string op, string inner, string appKey, string appSecret, CancellationToken cancellationToken)
    {
        var envelope =
            $"<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:sch=\"{SchemaNs}\">" +
            $"<soapenv:Header/><soapenv:Body><sch:{op}Request>" +
            $"<auth><appKey>{appKey}</appKey><appSecret>{appSecret}</appSecret></auth>{inner}" +
            $"</sch:{op}Request></soapenv:Body></soapenv:Envelope>";

        using var content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        content.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
        request.Headers.TryAddWithoutValidation("appkey", appKey);
        request.Headers.TryAddWithoutValidation("appsecret", appSecret);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException("TradeXpress:N11:CityFetchFailed")
                .WithData("op", op).WithData("status", (int)response.StatusCode);
        }

        return XDocument.Parse(body);
    }

    private static string Local(XElement parent, string localName)
    {
        var child = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
        return child?.Value.Trim() ?? string.Empty;
    }
}
