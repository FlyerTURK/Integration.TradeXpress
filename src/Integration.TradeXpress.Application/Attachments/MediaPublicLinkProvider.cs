using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Bir DAM medyası için DIŞARIDAN çekilebilir, SÜRELİ ve İMZALI bağlantı üretir/doğrular.
///
/// <para><b>Neden gerekli:</b> pazaryerleri (N11, Trendyol) görseli kendi sunucularından çeker; oturum
/// çerezi taşıyamazlar. DAM'ın normal içerik ucu oturum korumalıdır ve göreli adres döner — pazaryeri
/// erişemez. Bu yüzden yalnız medya okumaya açılan, imzayla doğrulanan ayrı bir adres üretilir.</para>
///
/// <para><b>Güvenlik biçimi:</b> erişim "korumasız" değil, kimlik yerine <b>imza</b> ile doğrulanır. Bağlantı
/// tek bir medyaya açılır, süresi vardır, tahmin edilemez ve listelenemez. İmza anahtarı yapılandırmadadır;
/// anahtar tanımlı değilse sağlayıcı bağlantı ÜRETMEZ (<c>null</c>) — yanlışlıkla zayıf imzayla açılmasın.</para>
///
/// <para>Bu, <c>PublicImageLinkProvider</c> içindeki 2026-07-07 "dışarıya uç açma" kararının bilinçli ve dar
/// istisnasıdır (2026-07-28 onayı): kapsam yalnız medya İÇERİĞİ okuma, yazma yok, listeleme yok.</para>
/// </summary>
public interface IMediaPublicLinkProvider
{
    /// <summary>Medya için süreli imzalı MUTLAK URL — anahtar/taban adres yapılandırılmamışsa <c>null</c>.</summary>
    string? TryCreateLink(Guid mediaId, Guid? tenantId, TimeSpan? lifetime = null);

    /// <summary>Bağlantıdaki jetonu doğrular. Geçerliyse medya VE tenant kimliğini döner; imza tutmuyorsa ya da
    /// süresi geçmişse <c>null</c> — çağıran ikisini AYIRMAZ (bilgi sızdırmamak için).</summary>
    MediaLinkTarget? TryResolveToken(string? token);
}

/// <summary>Jetondan çözülen hedef — medya ve ait olduğu tenant (host medyasında <c>null</c>).</summary>
public sealed record MediaLinkTarget(Guid MediaId, Guid? TenantId);

/// <inheritdoc cref="IMediaPublicLinkProvider"/>
public sealed class MediaPublicLinkProvider : IMediaPublicLinkProvider, ITransientDependency
{
    // Jeton biçimi: {mediaId:N}.{tenantId:N|"h"}.{expiryUnixSeconds}.{imza} — imza, önceki parçaların HMAC-SHA256'sı.
    //
    // TENANT jetonun İÇİNDE taşınır: uç oturumsuz çağrıldığında tenant bağlamı yoktur ve veri filtresi medyayı
    // bulamaz. Filtreyi DEVRE DIŞI bırakmak (yasak) yerine doğru bağlam açılır; jeton imzalı olduğundan tenant
    // kimliği de kurcalanamaz. Host medyası için "h" konur (tenant yok).
    private const char PartSeparator = '.';
    private const string HostMarker = "h";

    private readonly IConfiguration _configuration;

    public MediaPublicLinkProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string? TryCreateLink(Guid mediaId, Guid? tenantId, TimeSpan? lifetime = null)
    {
        if (mediaId == Guid.Empty)
        {
            return null;
        }

        var key = SigningKey;
        var baseUrl = BaseUrl;
        if (key.Length == 0 || baseUrl.Length == 0)
        {
            // Yapılandırma eksik → bağlantı üretilmez. Çağıran (push) bunu "kullanılamaz görsel" sayar.
            return null;
        }

        var expiry = DateTimeOffset.UtcNow.Add(lifetime ?? DefaultLifetime).ToUnixTimeSeconds();
        var payload = BuildPayload(mediaId, tenantId, expiry);
        var token = payload + PartSeparator + Sign(payload, key);

        return baseUrl.TrimEnd('/') + "/api/media/link/" + token;
    }

    public MediaLinkTarget? TryResolveToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parts = token.Split(PartSeparator);
        if (parts.Length != 4)
        {
            return null;
        }

        var key = SigningKey;
        if (key.Length == 0)
        {
            return null;
        }

        if (!Guid.TryParseExact(parts[0], "N", out var mediaId)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expiry))
        {
            return null;
        }

        Guid? tenantId = null;
        if (parts[1] != HostMarker)
        {
            if (!Guid.TryParseExact(parts[1], "N", out var parsedTenant))
            {
                return null;
            }

            tenantId = parsedTenant;
        }

        // İmza ÖNCE doğrulanır: süre kontrolünü imzasız veriye uygulamak, saldırganın süreyi düzenlemesine izin verirdi.
        var expected = Sign(BuildPayload(mediaId, tenantId, expiry), key);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[3])))
        {
            return null;
        }

        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= expiry
            ? new MediaLinkTarget(mediaId, tenantId)
            : null;
    }

    private static string BuildPayload(Guid mediaId, Guid? tenantId, long expiryUnixSeconds)
    {
        var tenantPart = tenantId is { } t ? t.ToString("N") : HostMarker;
        return mediaId.ToString("N")
            + PartSeparator + tenantPart
            + PartSeparator + expiryUnixSeconds.ToString(CultureInfo.InvariantCulture);
    }

    private static string Sign(string payload, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>URL-güvenli base64 — jeton adres yolunda taşınıyor, '+' ve '/' kaçış gerektirirdi.</summary>
    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private string SigningKey
    {
        get { return _configuration["MediaPublicLink:SigningKey"] ?? string.Empty; }
    }

    /// <summary>Mutlak adresin tabanı — pazaryeri göreli adresi çözemez.</summary>
    private string BaseUrl
    {
        get { return _configuration["MediaPublicLink:BaseUrl"] ?? string.Empty; }
    }

    private TimeSpan DefaultLifetime
    {
        get
        {
            var hours = _configuration.GetValue<int?>("MediaPublicLink:LifetimeHours") ?? 24;
            return TimeSpan.FromHours(hours < 1 ? 1 : hours);
        }
    }
}
