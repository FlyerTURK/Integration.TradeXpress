using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.SalesChannels.Etsy;

/// <summary>
/// Etsy API anahtarı (keystring) doğrulaması (server-side infra) — OAuth'suz public ping ucuna
/// (<c>GET /v3/application/openapi-ping</c>, header <c>x-api-key</c>) hafif bir çağrıyla anahtarın geçerliliğini sınar
/// (N11/Trendyol verifier'larıyla simetrik). Geçersiz ya da servise erişilemiyorsa tipli <c>BusinessException</c>
/// fırlatır; çağıran (Etsy AppService) bu durumda kaydı PERSIST ETMEZ.
/// NOT: SharedSecret bu uçla SINANAMAZ — OAuth token değişiminde dolaylı doğrulanır.
/// </summary>
public interface IEtsyCredentialVerifier
{
    /// <summary>Keystring geçerliyse sessizce döner; geçersizse <c>...:Etsy:InvalidCredentials</c>,
    /// servise erişilemiyorsa <c>...:Etsy:VerificationUnavailable</c> fırlatır.</summary>
    Task VerifyOrThrowAsync(string keystring, CancellationToken cancellationToken = default);
}
