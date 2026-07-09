using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.SalesChannels.Etsy;

/// <summary>Etsy OAuth token ucu (<see cref="EtsyOAuthConsts.TokenUrl"/>) HTTP istemcisi — authorization_code
/// değişimi + refresh_token yenilemesi (rotasyon: her yanıt YENİ refresh token içerir) + bağlantı-sonrası
/// mağaza bilgisi çözümü. Ham HTTP TEK burada yaşar (OAuthService/TokenProvider protokol detayını bilmez).</summary>
public interface IEtsyOAuthClient
{
    /// <summary>authorization_code + PKCE code_verifier → token çifti. Başarısızsa
    /// <c>...:Etsy:TokenExchangeFailed</c> (BusinessException) fırlatır.</summary>
    Task<EtsyTokenResult> ExchangeAuthorizationCodeAsync(
        string keystring,
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken = default);

    /// <summary>refresh_token grant'ı → YENİ token çifti (rotasyon — dönen refresh token'ı persist etmek çağıranın
    /// sorumluluğu). Başarısızsa <c>...:Etsy:TokenExchangeFailed</c> fırlatır.</summary>
    Task<EtsyTokenResult> RefreshAsync(
        string keystring,
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>Bağlanan kullanıcının mağazasını çözer (getMe → shop_id, getShop → shop_name). BEST-EFFORT:
    /// başarısızlık OAuth bağlantısını ETKİLEMEZ — (null, null) döner, uyarı loglanır.
    /// <paramref name="apiKeyHeader"/> = <c>{keystring}:{sharedSecret}</c> BİRLEŞİK x-api-key değeri (canlı teyitli
    /// Etsy gerekliliği — yalnız keystring 403 "Shared secret is required" verir).</summary>
    Task<(string? ShopId, string? ShopName)> TryGetShopInfoAsync(
        string apiKeyHeader,
        string accessToken,
        CancellationToken cancellationToken = default);
}

/// <summary>Token ucu yanıtı — access "{user_id}.{token}" biçimli; <paramref name="ExpiresInSeconds"/> access ömrü
/// (tipik 3600). Refresh ömrü yanıtta YOK → çağıran <see cref="EtsyOAuthConsts.RefreshTokenLifetimeDays"/>'ten hesaplar.</summary>
public sealed record EtsyTokenResult(string AccessToken, int ExpiresInSeconds, string RefreshToken);
