using System;
using System.Net.Http;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// GEÇİCİ GÖRSEL LİNKİ YAYINCISI — Hakan'ın tasarımı (CLAUDE.md §6, 2026-08-08): görsel geçici link veren bir
/// barındırmaya yüklenir → link satış kanalına verilir → kanal görseli KENDİ CDN'ine alır ve kendi linkini
/// döndürür. Kalıcı kamuya açık adres/domain ÖN KOŞUL DEĞİLDİR; ts.net imzalı linkleri pazaryeri EREMEZ
/// (tailnet-dışı), bu yüzden push görselleri buradan geçer.
///
/// <para><b>Yapılandırma</b> (<c>TemporaryMediaLink</c>): <c>Enabled</c> (varsayılan FALSE — test/mock
/// ortamları dış ağa çıkmasın; canlı host appsettings'te açar) · <c>Endpoint</c> (varsayılan litterbox —
/// 24 saate kadar geçici barındırma; Trendyol görseli ASENKRON çektiğinden 1 saatlik barındırıcılar riskli)
/// · <c>Lifetime</c> (litterbox değerleri: 1h/12h/24h/72h).</para>
///
/// <para><b>Hata duruşu:</b> yüklenemeyen görsel <c>null</c> döner ve loglanır — çağıran atlar (bir görselin
/// düşmesi push'un kalanını düşürmez; HİÇ görsel kalmazsa push'un kendi ImagesRequired fail-fast'i durdurur).</para>
/// </summary>
public class TemporaryMediaLinkPublisher : ITransientDependency
{
    public const string HttpClientName = "TemporaryMediaLink";
    private const string DefaultEndpoint = "https://litterbox.catbox.moe/resources/internals/api.php";
    private const string DefaultLifetime = "24h";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MediaContentReader _contentReader;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TemporaryMediaLinkPublisher> _logger;

    public TemporaryMediaLinkPublisher(
        IHttpClientFactory httpClientFactory,
        MediaContentReader contentReader,
        IConfiguration configuration,
        ILogger<TemporaryMediaLinkPublisher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _contentReader = contentReader;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Açık değilse push imzalı-link davranışına düşer (bugünkü durum) — kapalı ortamda dış ağ çağrısı YOK.</summary>
    public virtual bool IsEnabled
    {
        get { return _configuration.GetValue<bool>("TemporaryMediaLink:Enabled"); }
    }

    /// <summary>Medyayı geçici barındırmaya yükler ve dış URL döner; okunamayan/yüklenemeyen medyada <c>null</c>.
    /// KAPALIYKEN de <c>null</c> — dış ağa çıkılmaz. Bu kapı YAPISALDIR (çağıranın <see cref="IsEnabled"/>
    /// kontrolüne ek): ikinci bir çağıran (N11 portu, worker) eklendiğinde koruma sessizce kaybolmasın.</summary>
    public virtual async Task<string?> PublishAsync(Guid mediaId)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var payload = await _contentReader.ReadAsync(mediaId);
        if (payload is null)
        {
            _logger.LogWarning("Geçici link yayını atlandı — medya içeriği okunamadı (Media={MediaId}).", mediaId);
            return null;
        }

        try
        {
            var endpoint = _configuration["TemporaryMediaLink:Endpoint"] ?? DefaultEndpoint;
            var lifetime = _configuration["TemporaryMediaLink:Lifetime"] ?? DefaultLifetime;

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("fileupload"), "reqtype");
            form.Add(new StringContent(lifetime), "time");
            var file = new ByteArrayContent(payload.Bytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(payload.ContentType);
            form.Add(file, "fileToUpload", payload.FileName);

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsync(endpoint, form);
            response.EnsureSuccessStatusCode();

            var url = (await response.Content.ReadAsStringAsync()).Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp))
            {
                // Barındırıcı hata metni döndürmüş olabilir — onu URL sanıp kanala göndermek görseli
                // sessizce düşürür; null + log ile görünür kalır.
                _logger.LogWarning(
                    "Geçici link yayını URL döndürmedi (Media={MediaId}, Yanıt={Response}).",
                    mediaId, url.Length > 200 ? url[..200] : url);
                return null;
            }

            return url;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Geçici link yayını başarısız (Media={MediaId}).", mediaId);
            return null;
        }
    }
}
