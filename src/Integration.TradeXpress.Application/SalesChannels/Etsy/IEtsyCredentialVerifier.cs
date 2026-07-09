using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.SalesChannels.Etsy;

/// <summary>
/// Etsy API kimlik doğrulaması (server-side infra) — OAuth'suz public ping ucuna
/// (<c>GET /v3/application/openapi-ping</c>, header <c>x-api-key: {keystring}:{sharedSecret}</c> BİRLEŞİK format —
/// canlı doğrulanmış gerçek: Etsy yeni uygulamalarda secret'ı da x-api-key içinde İSTİYOR, "Shared secret is
/// required in x-api-key header" döner) hafif bir çağrıyla kimliğin geçerliliğini sınar (N11/Trendyol
/// verifier'larıyla simetrik). Geçersiz ya da servise erişilemiyorsa tipli <c>BusinessException</c> fırlatır;
/// çağıran (Etsy AppService) bu durumda kaydı PERSIST ETMEZ.
/// </summary>
public interface IEtsyCredentialVerifier
{
    /// <summary>Kimlik geçerliyse sessizce döner; geçersizse <c>...:Etsy:InvalidCredentials</c>,
    /// servise erişilemiyorsa <c>...:Etsy:VerificationUnavailable</c> fırlatır.</summary>
    Task VerifyOrThrowAsync(string keystring, string sharedSecret, CancellationToken cancellationToken = default);
}
