using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Geography;

/// <summary>Dataset il/eyalet satırı — provider'ın dışa verdiği sade kayıt (JSON şekli içeride kalır).</summary>
public sealed record GeographyStateRecord(string Name, string SubdivisionCode, string? Category);

/// <summary>Dataset şehir satırı — <see cref="StateCode"/> ilgili eyaletin ISO alt-bölüm kısaltmasıdır (ör. "34"/"AL").</summary>
public sealed record GeographyCityRecord(long Id, string Name, string? StateCode);

/// <summary>
/// dr5hn/countries-states-cities-database dataset sağlayıcısı — canlı API bağımlılığı YOK: dosyalar İLK ihtiyaçta
/// indirilip yerel önbelleğe alınır (<c>Geography:DatasetCacheDirectory</c>, default: content-root/App_Data/geography-cache),
/// sonraki importlar diskteki kopyadan okur. <c>json-cities.json.gz</c> (~25MB, 152.970 kayıt) ASLA topluca
/// materialize edilmez — GzipStream + <see cref="JsonSerializer.DeserializeAsyncEnumerable{TValue}(Stream, JsonSerializerOptions?, CancellationToken)"/>
/// (Utf8JsonReader tabanlı) ile eleman-eleman akıtılır, yalnız country_code eşleşenler toplanır (bellek dostu).
/// states.json küçük (~6.4MB) ama aynı akış deseniyle okunur (tutarlılık). İndirme hatası dostane
/// <see cref="BusinessException"/> (TradeXpress:Geography:DatasetUnavailable) olur.
/// </summary>
public class GeographyDatasetProvider : ITransientDependency
{
    #region Constants

    private const string ReleaseTagConfigKey = "Geography:DatasetReleaseTag";
    private const string CacheDirectoryConfigKey = "Geography:DatasetCacheDirectory";

    /// <summary>Pinned release (2026-07-11) — sürüm güncellemesi config'ten (yeni tag = yeni önbellek klasörü).</summary>
    private const string DefaultReleaseTag = "v3.2-export.6";

    private const string StatesFileName = "states.json";
    private const string CitiesFileName = "json-cities.json.gz";

    // states.json küçük olduğundan raw master'dan; şehirler büyük olduğundan pinned release asset'inden (gz).
    private const string StatesUrl =
        "https://raw.githubusercontent.com/dr5hn/countries-states-cities-database/master/json/states.json";

    private const string CitiesUrlTemplate =
        "https://github.com/dr5hn/countries-states-cities-database/releases/download/{0}/json-cities.json.gz";

    #endregion

    #region Fields

    // Dataset indirme seyrek (ülke başına ilk ihtiyaçta, dosyalar önbellekli) → paylaşılan tek HttpClient
    // (EtsyTaxonomyClient deseni). 25MB asset için geniş timeout.
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    // Web default'ları + sayı alanları string gelirse tolere et (dataset sürümleri arasında tip oynaması görüldü).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GeographyDatasetProvider> _logger;

    #endregion

    public GeographyDatasetProvider(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<GeographyDatasetProvider> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    #region Public API

    /// <summary>Önbellek dosyaları (states.json + json-cities.json.gz) yoksa indirir; varsa dokunmaz (idempotent).</summary>
    public virtual async Task EnsureDatasetAsync(CancellationToken cancellationToken = default)
    {
        var cacheDirectory = ResolveCacheDirectory();
        var releaseTag = ResolveReleaseTag();

        await EnsureFileAsync(
            Path.Combine(cacheDirectory, StatesFileName), StatesUrl, cancellationToken);
        await EnsureFileAsync(
            Path.Combine(cacheDirectory, CitiesFileName),
            string.Format(CitiesUrlTemplate, releaseTag),
            cancellationToken);
    }

    /// <summary>Ülkenin il/eyalet satırlarını yerel dosyadan akış-filtreli okur (alt-bölüm kodu boş satır atlanır).</summary>
    public virtual async Task<IReadOnlyList<GeographyStateRecord>> GetStatesForCountryAsync(
        string alpha2Code, CancellationToken cancellationToken = default)
    {
        await EnsureDatasetAsync(cancellationToken);

        var result = new List<GeographyStateRecord>();
        await using var file = OpenSequentialRead(Path.Combine(ResolveCacheDirectory(), StatesFileName));

        await foreach (var row in JsonSerializer.DeserializeAsyncEnumerable<StateRow>(file, JsonOptions, cancellationToken))
        {
            if (row == null || !MatchesCountry(row.CountryCode, alpha2Code))
            {
                continue;
            }

            // v3.2 export'ları alt-bölüm kısaltmasını "iso2" (yeni) ya da "state_code" (eski) alanında taşır.
            var subdivisionCode = FirstNonEmpty(row.Iso2, row.StateCode);
            if (string.IsNullOrWhiteSpace(row.Name) || subdivisionCode == null)
            {
                continue; // adsız/kodsuz satır işlenemez — dataset anomalisi, sessizce atla (import fail olmasın)
            }

            result.Add(new GeographyStateRecord(row.Name.Trim(), subdivisionCode, NormalizeCategory(row.Type)));
        }

        return result;
    }

    /// <summary>Ülkenin şehir satırlarını gzip'li dosyadan AKIŞLA okur — 152.970 kaydın tamamı asla belleğe
    /// alınmaz; yalnız country_code eşleşenler toplanır (adsız satır atlanır).</summary>
    public virtual async Task<IReadOnlyList<GeographyCityRecord>> GetCitiesForCountryAsync(
        string alpha2Code, CancellationToken cancellationToken = default)
    {
        await EnsureDatasetAsync(cancellationToken);

        var result = new List<GeographyCityRecord>();
        await using var file = OpenSequentialRead(Path.Combine(ResolveCacheDirectory(), CitiesFileName));
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);

        await foreach (var row in JsonSerializer.DeserializeAsyncEnumerable<CityRow>(gzip, JsonOptions, cancellationToken))
        {
            if (row == null || !MatchesCountry(row.CountryCode, alpha2Code) || string.IsNullOrWhiteSpace(row.Name))
            {
                continue;
            }

            result.Add(new GeographyCityRecord(row.Id, row.Name.Trim(), FirstNonEmpty(row.StateCode, null)));
        }

        return result;
    }

    /// <summary>Tek EYALETİN şehirlerini gzip'li dosyadan AKIŞLA okur — 152.970 kaydın tamamı asla belleğe alınmaz;
    /// yalnız country_code + state_code eşleşenler toplanır (per-state lazy import: US için 19k değil ~300 şehir).
    /// <paramref name="subdivisionCode"/> = idari alanın <c>Code</c>'u (dataset state_code'u ile hizalı, ör. "TN").</summary>
    public virtual async Task<IReadOnlyList<GeographyCityRecord>> GetCitiesForStateAsync(
        string alpha2Code, string subdivisionCode, CancellationToken cancellationToken = default)
    {
        await EnsureDatasetAsync(cancellationToken);

        var result = new List<GeographyCityRecord>();
        await using var file = OpenSequentialRead(Path.Combine(ResolveCacheDirectory(), CitiesFileName));
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);

        await foreach (var row in JsonSerializer.DeserializeAsyncEnumerable<CityRow>(gzip, JsonOptions, cancellationToken))
        {
            if (row == null
                || !MatchesCountry(row.CountryCode, alpha2Code)
                || !MatchesSubdivision(row.StateCode, subdivisionCode)
                || string.IsNullOrWhiteSpace(row.Name))
            {
                continue;
            }

            result.Add(new GeographyCityRecord(row.Id, row.Name.Trim(), FirstNonEmpty(row.StateCode, null)));
        }

        return result;
    }

    #endregion

    #region Download / cache

    // Önbellek kökü: config ya da content-root/App_Data/geography-cache; release tag alt klasörü (tag değişince
    // taze indirme). Göreli config yolu content-root'a göre çözülür.
    private string ResolveCacheDirectory()
    {
        var configured = _configuration[CacheDirectoryConfigKey];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(_environment.ContentRootPath, "App_Data", "geography-cache")
            : Path.GetFullPath(configured, _environment.ContentRootPath);

        var directory = Path.Combine(root, ResolveReleaseTag());
        Directory.CreateDirectory(directory);
        return directory;
    }

    private string ResolveReleaseTag()
    {
        var tag = _configuration[ReleaseTagConfigKey];
        return string.IsNullOrWhiteSpace(tag) ? DefaultReleaseTag : tag.Trim();
    }

    // Dosya yoksa indir: önce .tmp'ye yaz, sonra atomik taşı (yarım kalan indirme geçerli önbellek SAYILMASIN).
    private async Task EnsureFileAsync(string targetPath, string url, CancellationToken cancellationToken)
    {
        if (File.Exists(targetPath))
        {
            return;
        }

        var tempPath = targetPath + ".tmp";
        try
        {
            _logger.LogInformation("Coğrafya dataset indiriliyor: {Url} → {Target}", url, targetPath);

            using var response = await HttpClient.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var target = File.Create(tempPath))
            {
                await response.Content.CopyToAsync(target, cancellationToken);
            }

            File.Move(tempPath, targetPath, overwrite: true);
            _logger.LogInformation(
                "Coğrafya dataset indirildi: {File} ({Size:N0} bayt).",
                Path.GetFileName(targetPath), new FileInfo(targetPath).Length);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            TryDeleteQuietly(tempPath);
            // Kök neden loglanır + dostane iş hatası (UI toast'ı lokalize anahtardan konuşur); hata yutulmaz.
            _logger.LogError(ex, "Coğrafya dataset indirme HATASI: {Url}", url);
            throw new BusinessException("TradeXpress:Geography:DatasetUnavailable", innerException: ex)
                .WithData("url", url);
        }
    }

    private static FileStream OpenSequentialRead(string path)
    {
        return new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static void TryDeleteQuietly(string path)
    {
        // Yarım indirme artığını temizle; temizlik hatası asıl hatayı gölgelemesin (best-effort).
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // bilinçli: artık .tmp bir sonraki indirmede üzerine yazılır
        }
    }

    #endregion

    #region Row shapes & helpers

    private static bool MatchesCountry(string? countryCode, string alpha2Code)
    {
        return string.Equals(countryCode?.Trim(), alpha2Code, StringComparison.OrdinalIgnoreCase);
    }

    // Şehir state_code'u ile idari alanın kodunu eşler (per-state süzme). Boş state_code → eşleşmez (bir eyalete
    // bağlanamaz; ancak sembolik-ana alan ülke-geneli GetCitiesForCountryAsync ile toplandığından burada gerekmez).
    private static bool MatchesSubdivision(string? cityStateCode, string subdivisionCode)
    {
        return string.Equals(cityStateCode?.Trim(), subdivisionCode?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
    }

    // Kategori (type) opsiyonel: entity guard'ı (EnsureOptionalText min 2) tek karakterlik anomaliyi reddeder →
    // burada null'a düşür (import tek satır yüzünden patlamasın).
    private static string? NormalizeCategory(string? type)
    {
        var trimmed = type?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < 2)
        {
            return null;
        }

        return trimmed.Length <= GeographyConsts.CategoryMaxLength
            ? trimmed
            : trimmed.Substring(0, GeographyConsts.CategoryMaxLength);
    }

    /// <summary>states.json satırı (yalnız kullanılan alanlar; bilinmeyen alanlar yok sayılır).</summary>
    private sealed class StateRow
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }

        /// <summary>ISO 3166-2 alt-bölüm kısaltması (yeni export alanı; ör. TR için "34" değil "01"–"81").</summary>
        [JsonPropertyName("iso2")]
        public string? Iso2 { get; set; }

        /// <summary>Eski export'lardaki alt-bölüm kısaltması (iso2 yoksa fallback).</summary>
        [JsonPropertyName("state_code")]
        public string? StateCode { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    /// <summary>json-cities satırı (yalnız kullanılan alanlar; lat/long bilinçli dışarıda — şimdilik gereksiz).</summary>
    private sealed class CityRow
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; set; }

        [JsonPropertyName("state_code")]
        public string? StateCode { get; set; }
    }

    #endregion
}
