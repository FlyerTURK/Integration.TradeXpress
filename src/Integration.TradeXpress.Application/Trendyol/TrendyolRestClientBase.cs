using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Trendyol;

/// <summary>
/// Trendyol REST istemcileri için ORTAK TABAN — base URL + auth/User-Agent enjeksiyonu + gönderim. Trendyol V2 geçidi
/// (<c>apigw.trendyol.com</c>). Auth = Basic <c>base64(apiKey:apiSecret)</c>; ZORUNLU <c>User-Agent: "{sellerId} -
/// SelfIntegration"</c> (eksikse 403). SellerId path'e giren uçlarda <c>{sellerId}</c> kullanılır. HttpClient N11 ile
/// hizalı: paylaşılan static (kimlik çağrıları seyrek; soket tükenmesi yok). Kimlik/sir ASLA loglanmaz. Sürüm/uç
/// değişirse tek nokta <see cref="BaseUrl"/> ve türeyen istemcinin path sabitidir.
/// </summary>
public abstract class TrendyolRestClientBase
{
    /// <summary>Trendyol V2 API geçidi (V1 servisleri 2026-08 kapanıyor → baştan V2).</summary>
    protected const string BaseUrl = "https://apigw.trendyol.com";

    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Basic auth (apiKey:apiSecret) + zorunlu User-Agent "{sellerId} - SelfIntegration" enjekte edilmiş istek.</summary>
    protected static HttpRequestMessage CreateRequest(HttpMethod method, string url, TrendyolCredentials credentials)
    {
        var request = new HttpRequestMessage(method, url);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.ApiKey}:{credentials.ApiSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.UserAgent.ParseAdd($"{credentials.SellerId} - SelfIntegration");
        return request;
    }

    /// <summary>İsteği gönderir; (2xx mi, HTTP durumu, gövde) döner. Gövdeyi çağıran parse eder (hata dahil).</summary>
    protected static async Task<TrendyolResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            return new TrendyolResponse((int)response.StatusCode is >= 200 and < 300, (int)response.StatusCode, payload);
        }
    }
}

/// <summary>Ham Trendyol HTTP yanıtı — başarı bayrağı + durum + gövde.</summary>
public readonly record struct TrendyolResponse(bool Ok, int Status, string Payload);
