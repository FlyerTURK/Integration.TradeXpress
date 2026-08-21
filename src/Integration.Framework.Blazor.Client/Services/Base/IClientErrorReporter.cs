namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// Yakalanmış (toast'a düşmüş) teknik .NET hatalarını geliştirici tanılama paneline iletir.
/// Framework yalnız arayüzü tanımlar; uygulama implementasyonu kaydeder (ör. DevErrorSink →
/// Developer Error Panel). Blazor Server'da <c>ILogger</c> tarayıcı console'una gitmediğinden,
/// caught exception'ı panelde göstermenin yolu budur. Kayıtlı değilse çağrı sessizce atlanır.
/// </summary>
public interface IClientErrorReporter
{
    void Report(string message, string? detail = null);
}
