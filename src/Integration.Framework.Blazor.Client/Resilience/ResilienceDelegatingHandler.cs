using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Integration.Framework.Blazor.Client.Resilience;

/// <summary>
/// API çağrılarına geçici-hata dayanıklılığı: ağ kopması / timeout / 408,429,502,503,504 durumunda
/// kısa backoff'la otomatik yeniden dener. Mobil/uzak (Tailscale) bağlantılardaki anlık "blip"leri
/// kullanıcı görmeden yutar.
///
/// GÜVENLİK: yalnız IDEMPOTENT metotlar (GET/HEAD/OPTIONS/PUT/DELETE) yeniden denenir. POST/PATCH
/// denenmez — çünkü sunucuya ulaşmış olabilir ve tekrar çift kayıt/işlem üretebilir. 500 (handle
/// edilmemiş sunucu hatası) de denenmez; gerçek altyapı/transient kodlar denenir.
/// </summary>
public sealed class ResilienceDelegatingHandler : DelegatingHandler
{
    private const int MaxRetries = 2;
    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(900),
    };

    private readonly ILogger<ResilienceDelegatingHandler>? _logger;

    public ResilienceDelegatingHandler(ILogger<ResilienceDelegatingHandler>? logger = null)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var canRetry = IsIdempotent(request.Method);

        // İçeriği bir kez tamponla — yeniden gönderim için her denemede klon kurulur
        // (HttpRequestMessage aynı örnekle ikinci kez gönderilemez).
        byte[]? body = null;
        if (canRetry && request.Content != null)
            body = await request.Content.ReadAsByteArrayAsync(ct);

        for (var attempt = 0; ; attempt++)
        {
            var attemptRequest = attempt == 0 ? request : Clone(request, body);
            try
            {
                var response = await base.SendAsync(attemptRequest, ct);

                if (canRetry && attempt < MaxRetries && IsTransientStatus(response.StatusCode))
                {
                    _logger?.LogWarning("Resilience: retry {Attempt}/{Max} — {Method} {Uri} ({Status})",
                        attempt + 1, MaxRetries, request.Method, request.RequestUri, (int)response.StatusCode);
                    response.Dispose();
                    await Task.Delay(Backoff[attempt], ct);
                    continue;
                }
                return response;
            }
            catch (Exception ex) when (canRetry && attempt < MaxRetries && !ct.IsCancellationRequested && IsTransient(ex))
            {
                _logger?.LogWarning("Resilience: retry {Attempt}/{Max} — {Method} {Uri} ({Error})",
                    attempt + 1, MaxRetries, request.Method, request.RequestUri, ex.GetType().Name);
                await Task.Delay(Backoff[attempt], ct);
            }
        }
    }

    private static bool IsIdempotent(HttpMethod method)
        => method == HttpMethod.Get
        || method == HttpMethod.Head
        || method == HttpMethod.Options
        || method == HttpMethod.Put
        || method == HttpMethod.Delete;

    private static bool IsTransientStatus(HttpStatusCode code)
        => code == HttpStatusCode.RequestTimeout        // 408
        || code == (HttpStatusCode)429                  // Too Many Requests
        || code == HttpStatusCode.BadGateway            // 502
        || code == HttpStatusCode.ServiceUnavailable    // 503
        || code == HttpStatusCode.GatewayTimeout;       // 504

    // Yanıt alınamadan kopan istekler: ağ hatası / timeout.
    private static bool IsTransient(Exception ex)
        => ex is HttpRequestException
        || ex is TimeoutException
        || ex is TaskCanceledException;

    private static HttpRequestMessage Clone(HttpRequestMessage req, byte[]? body)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri) { Version = req.Version };

        if (body != null)
        {
            clone.Content = new ByteArrayContent(body);
            if (req.Content != null)
                foreach (var h in req.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        foreach (var h in req.Headers)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);

        foreach (var opt in (IDictionary<string, object?>)req.Options)
            ((IDictionary<string, object?>)clone.Options)[opt.Key] = opt.Value;

        return clone;
    }
}
