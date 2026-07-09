namespace Integration.TradeXpress.SalesChannels.Etsy;

/// <summary>
/// Etsy Open API v3 + OAuth 2.0 (Authorization Code + PKCE S256) sabitleri — uç/sürüm değişirse TEK nokta burasıdır.
/// Redirect URI çalışma anında <c>App:SelfUrl</c> + <see cref="CallbackPath"/>'ten türetilir (kanonik:
/// https://umut.taile7a850.ts.net:44318/etsy/oauth-callback — Etsy uygulama kaydında birebir tanımlı olmalı,
/// case-sensitive + trailing-slash'siz).
/// </summary>
public static class EtsyOAuthConsts
{
    /// <summary>Satıcı onay sayfası (authorize URL) — kullanıcı buraya yönlendirilir.</summary>
    public const string AuthorizeUrl = "https://www.etsy.com/oauth/connect";

    /// <summary>Token değişim ucu (authorization_code + refresh_token grant'ları).</summary>
    public const string TokenUrl = "https://api.etsy.com/v3/public/oauth/token";

    /// <summary>API kökü (uygulama uçları /v3/application/... altında).</summary>
    public const string ApiBaseUrl = "https://api.etsy.com/v3";

    /// <summary>Blazor host'ta OAuth callback endpoint path'i (App:SelfUrl ile birleşip redirect_uri olur).</summary>
    public const string CallbackPath = "/etsy/oauth-callback";

    /// <summary>İstenen scope'lar (boşluk-ayrımlı; authorize URL'de URL-encode edilir). E1 kapsamı: ürün/mağaza/işlem
    /// okuma-yazma — sonraki dilimler (push/sipariş) için yeterli, satıcıya İKİNCİ onay ekranı çıkarmamak adına baştan.</summary>
    public const string Scopes = "listings_r listings_w shops_r shops_w transactions_r";

    /// <summary>PKCE state/verifier geçici saklama süresi — satıcının onay ekranını tamamlaması için makul pencere.</summary>
    public const int StateCacheMinutes = 10;

    /// <summary>Refresh token ömrü (Etsy: 90 gün; her yenilemede YENİ refresh token döner — rotasyon).
    /// Token yanıtında refresh ömrü ayrıca dönmez → sabitten hesaplanır.</summary>
    public const int RefreshTokenLifetimeDays = 90;

    /// <summary>Access token süre payı (saniye): bitişe bu kadar kala "süresi dolmuş" sayılır (saat kayması/ağ gecikmesi).</summary>
    public const int AccessTokenExpirySkewSeconds = 120;
}
