using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Integration.TradeXpress.N11Products;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.SalesChannels.N11;

/// <summary>
/// N11 kimlik doğrulayıcı — <b>CategoryService.GetTopLevelCategories</b> SOAP çağrısıyla AppKey/AppSecret'ı sınar.
/// GÖZLEM (canlı probe): N11 API geçidi (openresty) GEÇERSİZ kimlikte <c>HTTP 403 + "Authentication failed"</c>
/// döndürür; geçerli kimlikte HTTP 200 + kategori gövdesi. Bu yüzden birincil sinyal HTTP durumudur:
/// 401/403 (ya da "authentication failed" gövdesi) → geçersiz; 200 → geçerli (gövdede açık <c>status=failure</c>
/// varsa yine geçersiz). Ağ / timeout / diğer durum kodları → "doğrulanamadı" (transient; çağıran persist ETMEZ).
/// Kimlik geçitten hem SOAP gövdesinde hem header'da gönderilir (yeni gateway header okuyabilir). Sir ASLA loglanmaz.
/// </summary>
// Sınıf adı arayüz-konvansiyonuna (I{ClassName}) uymadığından ABP arayüzü otomatik expose ETMEZ → açıkça bildir.
[ExposeServices(typeof(IN11CredentialVerifier))]
public sealed class N11CategoryServiceCredentialVerifier : IN11CredentialVerifier, ITransientDependency
{
    // N11 SOAP CategoryService uç noktası (WSDL == servis adresi). Sürüm/uç değişirse tek nokta burasıdır.
    private const string N11SchemaNamespace = "http://www.n11.com/ws/schemas";

    // Kimlik kontrolü seyrek çağrılır (yalnız yeni anahtar girilince) → paylaşılan tek HttpClient yeterli
    // (soket tükenmesi yok; IHttpClientFactory bağımlılığı gereksiz).
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly ILogger<N11CategoryServiceCredentialVerifier> _logger;

    // Uç adresi N11EndpointOptions'tan gelir (varsayılan https://api.n11.com). Sahte sunucu kipinde kimlik
    // doğrulaması da oraya gitmelidir — aksi halde mock kanal HİÇ kaydedilemez (create doğrulamadan geçmez).
    private readonly N11EndpointOptions _endpoints;

    private string CategoryServiceEndpoint
    {
        get { return _endpoints.CategoryServiceEndpoint; }
    }

    public N11CategoryServiceCredentialVerifier(
        ILogger<N11CategoryServiceCredentialVerifier> logger, IOptions<N11EndpointOptions> endpointOptions)
    {
        _logger = logger;
        _endpoints = endpointOptions.Value;
    }

    public async Task VerifyOrThrowAsync(string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var result = await ProbeAsync(appKey, appSecret, cancellationToken);
        if (result == ProbeResult.Valid)
        {
            return;
        }

        if (result == ProbeResult.Invalid)
        {
            throw new BusinessException("TradeXpress:SalesChannel:N11:InvalidCredentials");
        }

        throw new BusinessException("TradeXpress:SalesChannel:N11:VerificationUnavailable");
    }

    private async Task<ProbeResult> ProbeAsync(string appKey, string appSecret, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, CategoryServiceEndpoint)
            {
                Content = new StringContent(BuildEnvelope(appKey, appSecret), Encoding.UTF8, "text/xml"),
            };
            request.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");   // SOAP 1.1: boş action
            request.Headers.TryAddWithoutValidation("appkey", appKey);       // yeni gateway header auth (gövdeye ek)
            request.Headers.TryAddWithoutValidation("appsecret", appSecret);
            request.Headers.TryAddWithoutValidation("User-Agent", "TradeXpress/1.0");

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // Geçersiz kimlik → N11 geçidi HTTP 401/403 + "Authentication failed" (gözlemlenen davranış).
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized ||
                body.Contains("authentication failed", StringComparison.OrdinalIgnoreCase))
            {
                return ProbeResult.Invalid;
            }

            if (response.IsSuccessStatusCode)
            {
                // 200 = geçit kimliği geçirdi → geçerli. Gövdede açık status=failure varsa yine geçersiz say.
                return ReadStatus(body) == ProbeResult.Invalid ? ProbeResult.Invalid : ProbeResult.Valid;
            }

            _logger.LogWarning("N11 kimlik doğrulama beklenmeyen HTTP {Status} döndü.", (int)response.StatusCode);
            return ProbeResult.Unavailable;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "N11 kimlik doğrulama servisine erişilemedi.");
            return ProbeResult.Unavailable;
        }
    }

    // result/status metnini namespace-agnostik (LocalName) oku: failure → geçersiz; aksi halde belirsiz/geçerli.
    private static ProbeResult ReadStatus(string soapBody)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(soapBody);
        }
        catch (XmlException)
        {
            return ProbeResult.Unavailable;
        }

        foreach (var element in document.Descendants())
        {
            if (!string.Equals(element.Name.LocalName, "status", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = element.Value.Trim();
            if (value.Equals("success", StringComparison.OrdinalIgnoreCase))
            {
                return ProbeResult.Valid;
            }

            if (value.Equals("failure", StringComparison.OrdinalIgnoreCase))
            {
                return ProbeResult.Invalid;
            }
        }

        return ProbeResult.Unavailable;
    }

    // SOAP 1.1 zarfı — GetTopLevelCategoriesRequest yalnız auth alır (XLinq ile XML-escape güvenli inşa).
    private static string BuildEnvelope(string appKey, string appSecret)
    {
        var request = new XElement(XName.Get("GetTopLevelCategoriesRequest", N11SchemaNamespace),
            new XElement("auth",
                new XElement("appKey", appKey),
                new XElement("appSecret", appSecret)));

        XNamespace soapenv = "http://schemas.xmlsoap.org/soap/envelope/";
        var envelope = new XElement(soapenv + "Envelope",
            new XElement(soapenv + "Header"),
            new XElement(soapenv + "Body", request));

        return new XDocument(envelope).ToString(SaveOptions.DisableFormatting);
    }

    private enum ProbeResult
    {
        Valid,
        Invalid,
        Unavailable,
    }
}
