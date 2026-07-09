using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.SalesChannels.Etsy;

/// <summary>
/// <see cref="IEtsyOAuthClient"/> implementasyonu — form-encoded POST ile token değişimi/yenilemesi + best-effort
/// mağaza çözümü. Sir/token ASLA loglanmaz (yalnız HTTP durum + Etsy'nin error alanı). Uçlar <see cref="EtsyOAuthConsts"/>'ta.
/// </summary>
public sealed class EtsyOAuthClient : IEtsyOAuthClient, ITransientDependency
{
    // OAuth çağrıları seyrek (bağlan + saatte bir refresh) → paylaşılan tek HttpClient yeterli (verifier deseni).
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly ILogger<EtsyOAuthClient> _logger;

    public EtsyOAuthClient(ILogger<EtsyOAuthClient> logger)
    {
        _logger = logger;
    }

    public Task<EtsyTokenResult> ExchangeAuthorizationCodeAsync(
        string keystring,
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        return RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = keystring,
                ["redirect_uri"] = redirectUri,
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
            },
            cancellationToken);
    }

    public Task<EtsyTokenResult> RefreshAsync(
        string keystring,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        return RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = keystring,
                ["refresh_token"] = refreshToken,
            },
            cancellationToken);
    }

    public async Task<(string? ShopId, string? ShopName)> TryGetShopInfoAsync(
        string keystring,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // getMe → shop_id (scope: shops_r). Sonra getShop → shop_name (görüntü alanı).
            using var meDoc = await GetJsonAsync($"{EtsyOAuthConsts.ApiBaseUrl}/application/users/me", keystring, accessToken, cancellationToken);
            if (meDoc == null || !meDoc.RootElement.TryGetProperty("shop_id", out var shopIdElement))
            {
                return (null, null);
            }

            var shopId = shopIdElement.ValueKind == JsonValueKind.Number
                ? shopIdElement.GetInt64().ToString()
                : shopIdElement.GetString();
            if (string.IsNullOrEmpty(shopId))
            {
                return (null, null);
            }

            using var shopDoc = await GetJsonAsync($"{EtsyOAuthConsts.ApiBaseUrl}/application/shops/{Uri.EscapeDataString(shopId)}", keystring, accessToken, cancellationToken);
            var shopName = shopDoc != null && shopDoc.RootElement.TryGetProperty("shop_name", out var nameElement)
                ? nameElement.GetString()
                : null;

            return (shopId, shopName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Etsy mağaza bilgisi çözülemedi (best-effort — bağlantı etkilenmez).");
            return (null, null);
        }
    }

    /// <summary>x-api-key + Bearer başlıklı GET → JSON belge (başarısız durum kodunda null — best-effort çağıranlar için).</summary>
    private static async Task<JsonDocument?> GetJsonAsync(
        string url, string keystring, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("x-api-key", keystring);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(json);
    }

    /// <summary>Token ucuna form-encoded POST — başarıda access/expires_in/refresh üçlüsünü döner, aksi hâlde dostane
    /// TokenExchangeFailed (Etsy'nin error alanı loglanır; token/sır loglanmaz).</summary>
    private async Task<EtsyTokenResult> RequestTokenAsync(
        Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        string body;
        System.Net.HttpStatusCode status;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, EtsyOAuthConsts.TokenUrl)
            {
                Content = new FormUrlEncodedContent(form),
            };

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogTokenError(status, body, form["grant_type"]);
                throw new BusinessException("TradeXpress:SalesChannel:Etsy:TokenExchangeFailed");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Etsy token ucuna erişilemedi.");
            throw new BusinessException("TradeXpress:SalesChannel:Etsy:TokenExchangeFailed");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var accessToken = root.GetProperty("access_token").GetString();
            var refreshToken = root.GetProperty("refresh_token").GetString();
            var expiresIn = root.GetProperty("expires_in").GetInt32();

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
            {
                throw new BusinessException("TradeXpress:SalesChannel:Etsy:TokenExchangeFailed");
            }

            return new EtsyTokenResult(accessToken, expiresIn, refreshToken);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Etsy token yanıtı beklenen biçimde değil (HTTP {Status}).", (int)status);
            throw new BusinessException("TradeXpress:SalesChannel:Etsy:TokenExchangeFailed");
        }
    }

    /// <summary>Hata gövdesinden yalnız Etsy'nin <c>error</c>/<c>error_description</c> alanlarını loglar (token/sır içermez).</summary>
    private void LogTokenError(System.Net.HttpStatusCode status, string body, string grantType)
    {
        string? error = null;
        string? description = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
            {
                error = e.GetString();
            }

            if (doc.RootElement.TryGetProperty("error_description", out var d))
            {
                description = d.GetString();
            }
        }
        catch (JsonException)
        {
            // Gövde JSON değil — yalnız durum kodu loglanır.
        }

        _logger.LogWarning(
            "Etsy token isteği başarısız (grant={Grant}, HTTP {Status}, error={Error}, description={Description}).",
            grantType, (int)status, error, description);
    }
}
