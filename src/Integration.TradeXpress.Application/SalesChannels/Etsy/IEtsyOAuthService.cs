using System;
using System.Threading.Tasks;

namespace Integration.TradeXpress.SalesChannels.Etsy;

/// <summary>
/// Etsy OAuth 2.0 (Authorization Code + PKCE) akış orkestratörü — <b>başlat</b> (state/verifier üret + geçici sakla +
/// authorize URL kur) ve <b>callback</b> (state doğrula → code'u token'a çevir → kanala yaz). Callback endpoint'i
/// Blazor host'ta (<c>/etsy/oauth-callback</c>) minimal-API olarak haritalanır ve bu servise delege eder.
/// </summary>
public interface IEtsyOAuthService
{
    /// <summary>Kanal için akışı başlatır: PKCE code_verifier + CSRF state üretir, dağıtık cache'e koyar
    /// (TTL <see cref="EtsyOAuthConsts.StateCacheMinutes"/> dk) ve satıcının yönlendirileceği authorize URL'ini döner.</summary>
    Task<string> StartAsync(SalesChannelEtsy channel);

    /// <summary>Etsy geri dönüşünü işler: state→cache lookup (tek kullanımlık; CSRF koruması) → token değişimi
    /// (PKCE verifier ile) → token'ları İLGİLİ kanala atomik yazar (+ best-effort mağaza bilgisi). ASLA fırlatmaz —
    /// endpoint sonucu kullanıcı yönlendirmesine çevirir (hatalar loglanır).</summary>
    Task<EtsyOAuthCallbackResult> HandleCallbackAsync(string? state, string? code, string? error);
}

/// <summary>Callback işleme sonucu — endpoint bunu redirect URL'ine çevirir (başarı/hata mesajı UI'da gösterilir).
/// <paramref name="ChannelId"/> state çözülemediyse null (hangi kanal olduğu bilinemez → genel listeye dön).</summary>
public sealed record EtsyOAuthCallbackResult(bool Success, Guid? ChannelId);
