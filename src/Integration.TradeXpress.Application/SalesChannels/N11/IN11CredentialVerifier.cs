using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.SalesChannels.N11;

/// <summary>
/// N11 API kimlik doğrulaması (server-side infra) — verilen AppKey/AppSecret ile hafif bir N11 çağrısı yapıp
/// geçerliliği sınar. Geçersiz ya da servise erişilemiyorsa tipli <c>BusinessException</c> fırlatır; çağıran
/// (N11 AppService) bu durumda kaydı PERSIST ETMEZ. Yalnız N11 için vardır — Trendyol'da test API'si yok.
/// </summary>
public interface IN11CredentialVerifier
{
    /// <summary>Kimlik geçerliyse sessizce döner; geçersizse <c>...:N11:InvalidCredentials</c>,
    /// servise erişilemiyorsa <c>...:N11:VerificationUnavailable</c> fırlatır.</summary>
    Task VerifyOrThrowAsync(string appKey, string appSecret, CancellationToken cancellationToken = default);
}
