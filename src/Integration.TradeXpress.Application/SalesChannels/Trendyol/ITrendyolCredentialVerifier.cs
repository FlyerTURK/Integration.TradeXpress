using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.SalesChannels.Trendyol;

/// <summary>
/// Trendyol API kimlik doğrulaması (server-side infra) — verilen SellerId/ApiKey/ApiSecret ile hafif bir Trendyol
/// çağrısı yapıp geçerliliği sınar (N11 <c>IN11CredentialVerifier</c> ile simetrik). Geçersiz ya da servise
/// erişilemiyorsa tipli <c>BusinessException</c> fırlatır; çağıran (Trendyol AppService) bu durumda kaydı PERSIST
/// ETMEZ. Trendyol'da SellerId path'te olduğundan probe hem kimliği hem SellerId'yi teyit eder.
/// </summary>
public interface ITrendyolCredentialVerifier
{
    /// <summary>Kimlik geçerliyse sessizce döner; geçersizse <c>...:Trendyol:InvalidCredentials</c>,
    /// servise erişilemiyorsa <c>...:Trendyol:VerificationUnavailable</c> fırlatır.</summary>
    Task VerifyOrThrowAsync(string sellerId, string apiKey, string apiSecret, CancellationToken cancellationToken = default);
}
