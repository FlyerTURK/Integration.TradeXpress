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

    /// <summary>Trendyol geçidi agresif 429 (Too Many Requests) verir — özellikle çok sayfa/pencere çeken salt-GET
    /// akışlarında (canlı doğrulandı: ~3 istek/sn'de bile 429). Bu sayı 429'da kaç kez bekleyip yeniden deneneceğidir;
    /// tükenince son (429) yanıt döner ve çağıran dostane hata fırlatır (sessiz kısmi sonuç YOK).</summary>
    private const int MaxRetryOn429 = 6;

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

    /// <summary>GET'i 429'a DAYANIKLI gönderir: her denemede TAZE istek kurar (HttpRequestMessage tek-kullanımlık),
    /// 429'da <c>Retry-After</c> header'ı kadar (yoksa exponential backoff, max 60s) bekleyip tekrar dener. Salt-GET
    /// olduğundan yeniden deneme güvenlidir. POST/yazma bu yolu KULLANMAZ (idempotent değil).</summary>
    protected static async Task<TrendyolResponse> SendGetWithRetryAsync(
        string url, TrendyolCredentials credentials, CancellationToken cancellationToken)
    {
        var backoffSeconds = 5;
        for (var attempt = 1; ; attempt++)
        {
            using var request = CreateRequest(HttpMethod.Get, url, credentials);
            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode == 429 && attempt < MaxRetryOn429)
            {
                var wait = ResolveRetryDelaySeconds(response, backoffSeconds);
                backoffSeconds = Math.Min(backoffSeconds * 2, 60);
                await Task.Delay(TimeSpan.FromSeconds(wait), cancellationToken);
                continue;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            return new TrendyolResponse((int)response.StatusCode is >= 200 and < 300, (int)response.StatusCode, payload);
        }
    }

    /// <summary>429 yanıtındaki <c>Retry-After</c>'ı saniyeye çevirir (delta ya da tarih biçimi); yoksa fallback backoff.</summary>
    private static int ResolveRetryDelaySeconds(HttpResponseMessage response, int fallbackSeconds)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return fallbackSeconds;
        }

        if (retryAfter.Delta is { } delta)
        {
            return Math.Max(1, (int)delta.TotalSeconds);
        }

        if (retryAfter.Date is { } date)
        {
            return Math.Max(1, (int)(date - DateTimeOffset.UtcNow).TotalSeconds);
        }

        return fallbackSeconds;
    }
}

/// <summary>Ham Trendyol HTTP yanıtı — başarı bayrağı + durum + gövde.</summary>
public readonly record struct TrendyolResponse(bool Ok, int Status, string Payload);
