using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.SalesChannels.Etsy;

/// <summary>
/// Etsy keystring doğrulayıcı — OAuth'suz public <b>ping</b> ucu (<c>GET /v3/application/openapi-ping</c>,
/// header <c>x-api-key: {keystring}</c>). 2xx → anahtar geçerli. SharedSecret bu uçla sınanamaz (OAuth token
/// değişiminde dolaylı doğrulanır) — probe yalnız keystring'i teyit eder.
///
/// <para>Durum yorumu (N11/Trendyol verifier'larıyla simetrik): 401/403 → geçersiz; 2xx → geçerli; ağ/timeout/diğer
/// durum → belirsiz → çağıran persist ETMEZ. Sir ASLA loglanmaz. Endpoint/sürüm değişirse tek nokta
/// <see cref="EtsyOAuthConsts"/>.</para>
/// </summary>
// Sınıf adı arayüz-konvansiyonuna (I{ClassName}) uymadığından ABP arayüzü otomatik expose ETMEZ → açıkça bildir.
[ExposeServices(typeof(IEtsyCredentialVerifier))]
public sealed class EtsyPingCredentialVerifier : IEtsyCredentialVerifier, ITransientDependency
{
    // Kimlik kontrolü seyrek çağrılır (yalnız yeni kimlik girilince) → paylaşılan tek HttpClient yeterli
    // (soket tükenmesi yok; IHttpClientFactory bağımlılığı gereksiz). N11/Trendyol verifier ile aynı yaklaşım.
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly ILogger<EtsyPingCredentialVerifier> _logger;

    public EtsyPingCredentialVerifier(ILogger<EtsyPingCredentialVerifier> logger)
    {
        _logger = logger;
    }

    public async Task VerifyOrThrowAsync(string keystring, CancellationToken cancellationToken = default)
    {
        var result = await ProbeAsync(keystring, cancellationToken);
        if (result == ProbeResult.Valid)
        {
            return;
        }

        if (result == ProbeResult.Invalid)
        {
            throw new BusinessException("TradeXpress:SalesChannel:Etsy:InvalidCredentials");
        }

        throw new BusinessException("TradeXpress:SalesChannel:Etsy:VerificationUnavailable");
    }

    private async Task<ProbeResult> ProbeAsync(string keystring, CancellationToken cancellationToken)
    {
        try
        {
            // Public ping — OAuth gerektirmez; yalnız x-api-key başlığını sınar (dokümante keystring doğrulama yolu).
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{EtsyOAuthConsts.ApiBaseUrl}/application/openapi-ping");
            request.Headers.TryAddWithoutValidation("x-api-key", keystring);

            using var response = await HttpClient.SendAsync(request, cancellationToken);

            // Geçersiz/bilinmeyen anahtar → Etsy 401 (Unauthorized) ya da 403 (Forbidden) döner.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return ProbeResult.Invalid;
            }

            if (response.IsSuccessStatusCode)
            {
                return ProbeResult.Valid;
            }

            _logger.LogWarning("Etsy keystring doğrulama beklenmeyen HTTP {Status} döndü.", (int)response.StatusCode);
            return ProbeResult.Unavailable;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Etsy doğrulama servisine erişilemedi.");
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
