using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.SalesChannels.Trendyol;

/// <summary>
/// Trendyol kimlik doğrulayıcı — hafif bir <b>authenticated GET</b> (ürün listeleme, sayfa 0 / boyut 1) ile
/// SellerId + ApiKey + ApiSecret'ı sınar. SellerId path'te olduğundan probe hem kimliği hem SellerId'yi teyit eder
/// (yanlış SellerId de yakalanır). Auth = Basic <c>base64(apiKey:apiSecret)</c>; Trendyol'un ZORUNLU kıldığı
/// <c>User-Agent: "{sellerId} - SelfIntegration"</c> gönderilir (eksikse 403).
///
/// <para>Durum yorumu (N11 verifier ile simetrik): 401/403 → geçersiz; 2xx → geçerli; ağ/timeout/diğer durum
/// (404 dâhil — endpoint/sürüm sapması transient sayılır, yanlış-negatif yerine "doğrulanamadı") → belirsiz →
/// çağıran persist ETMEZ. Sir ASLA loglanmaz. Endpoint/base sürümü değişirse tek nokta burasıdır.</para>
/// </summary>
// Sınıf adı arayüz-konvansiyonuna (I{ClassName}) uymadığından ABP arayüzü otomatik expose ETMEZ → açıkça bildir.
[ExposeServices(typeof(ITrendyolCredentialVerifier))]
public sealed class TrendyolProductServiceCredentialVerifier : ITrendyolCredentialVerifier, ITransientDependency
{
    // Trendyol V2 API geçidi (V1 servisleri 2026-08 kapanıyor → baştan V2). Sürüm/uç değişirse tek nokta burasıdır.
    private const string ApiBaseUrl = "https://apigw.trendyol.com";

    // Kimlik kontrolü seyrek çağrılır (yalnız yeni kimlik girilince) → paylaşılan tek HttpClient yeterli
    // (soket tükenmesi yok; IHttpClientFactory bağımlılığı gereksiz). N11 verifier ile aynı yaklaşım.
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly ILogger<TrendyolProductServiceCredentialVerifier> _logger;

    public TrendyolProductServiceCredentialVerifier(ILogger<TrendyolProductServiceCredentialVerifier> logger)
    {
        _logger = logger;
    }

    public async Task VerifyOrThrowAsync(
        string sellerId,
        string apiKey,
        string apiSecret,
        CancellationToken cancellationToken = default)
    {
        var result = await ProbeAsync(sellerId, apiKey, apiSecret, cancellationToken);
        if (result == ProbeResult.Valid)
        {
            return;
        }

        if (result == ProbeResult.Invalid)
        {
            throw new BusinessException("TradeXpress:SalesChannel:Trendyol:InvalidCredentials");
        }

        throw new BusinessException("TradeXpress:SalesChannel:Trendyol:VerificationUnavailable");
    }

    private async Task<ProbeResult> ProbeAsync(
        string sellerId,
        string apiKey,
        string apiSecret,
        CancellationToken cancellationToken)
    {
        try
        {
            // Hafif authenticated GET: 1 kayıt iste (yük minimum). SellerId path'te → kimlik + SellerId birlikte sınanır.
            var url = $"{ApiBaseUrl}/integration/product/sellers/{Uri.EscapeDataString(sellerId)}/products?page=0&size=1";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            // Trendyol ZORUNLU User-Agent formatı — eksikse 403 (kimlikten bağımsız). SellerId burada da geçer.
            request.Headers.TryAddWithoutValidation("User-Agent", $"{sellerId} - SelfIntegration");

            using var response = await HttpClient.SendAsync(request, cancellationToken);

            // Geçersiz kimlik / yanlış SellerId → Trendyol geçidi 401 (Unauthorized) veya 403 (Forbidden).
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return ProbeResult.Invalid;
            }

            if (response.IsSuccessStatusCode)
            {
                return ProbeResult.Valid;
            }

            _logger.LogWarning("Trendyol kimlik doğrulama beklenmeyen HTTP {Status} döndü.", (int)response.StatusCode);
            return ProbeResult.Unavailable;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Trendyol kimlik doğrulama servisine erişilemedi.");
            return ProbeResult.Unavailable;
        }
    }

    private enum ProbeResult
    {
        Valid,
        Invalid,
        Unavailable,
    }
}
