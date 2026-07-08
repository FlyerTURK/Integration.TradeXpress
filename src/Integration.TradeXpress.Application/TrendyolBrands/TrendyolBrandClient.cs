using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Trendyol;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.TrendyolBrands;

/// <summary>
/// <see cref="ITrendyolBrandClient"/> — Trendyol V2 REST, yalnız GET (pazaryerine SIFIR yazma). Ada göre arama SSOT;
/// marka verisi saklanmaz. Yanıt şekli <c>{ "brands": [ { "id": 123, "name": "..." } ] }</c>. Auth (Basic + zorunlu
/// User-Agent) taban tarafından eklenir; kimlik/sır ASLA loglanmaz.
/// </summary>
public sealed class TrendyolBrandClient : TrendyolRestClientBase, ITrendyolBrandClient, ITransientDependency
{
    /// <summary>by-name endpoint yoksa (404) kullanılan fallback tarama penceresi. Trendyol brands sayfası alfabetik
    /// ilk N markayı verir → contains-filtre TAM tarama DEĞİL, yalnız degrade best-effort ağdır (by-name canlıda
    /// çalışırsa hiç tetiklenmez).</summary>
    private const int FallbackScanSize = 1000;

    private readonly ILogger<TrendyolBrandClient> _logger;

    public TrendyolBrandClient(ILogger<TrendyolBrandClient> logger)
    {
        _logger = logger;
    }

    public async Task<List<TrendyolBrandDto>> SearchByNameAsync(
        TrendyolCredentials credentials, string name, int size = 20, CancellationToken cancellationToken = default)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new List<TrendyolBrandDto>();
        }

        // 1) Birincil: ada göre arama (server-side). Boş/404 → sayfalı fallback.
        var byNameUrl = $"{BaseUrl}/integration/product/brands/by-name?name={Uri.EscapeDataString(trimmed)}";
        using (var request = CreateRequest(HttpMethod.Get, byNameUrl, credentials))
        {
            var response = await SendAsync(request, cancellationToken);
            if (response.Ok)
            {
                var matches = ParseBrands(response.Payload);
                if (matches.Count > 0)
                {
                    return matches.Take(size).ToList();
                }
            }
            else if (response.Status != (int)HttpStatusCode.NotFound)
            {
                // 401/403/5xx = gerçek hata (yalnız 404 = endpoint yok → fallback meşru).
                _logger.LogWarning("Trendyol marka araması (by-name) başarısız (HTTP {Status}).", response.Status);
                throw new BusinessException("TradeXpress:Trendyol:Brand:SearchFailed").WithData("status", response.Status);
            }
        }

        // 2) Fallback: sayfalı brands + aksan/case-duyarsız contains-filtre (best-effort).
        return await ScanAndFilterAsync(credentials, trimmed, size, cancellationToken);
    }

    private async Task<List<TrendyolBrandDto>> ScanAndFilterAsync(
        TrendyolCredentials credentials, string name, int size, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/integration/product/brands?page=0&size={FallbackScanSize}";
        using var request = CreateRequest(HttpMethod.Get, url, credentials);
        var response = await SendAsync(request, cancellationToken);
        if (!response.Ok)
        {
            _logger.LogWarning("Trendyol marka araması (sayfalı fallback) başarısız (HTTP {Status}).", response.Status);
            throw new BusinessException("TradeXpress:Trendyol:Brand:SearchFailed").WithData("status", response.Status);
        }

        var needle = NormalizeForSearch(name);
        return ParseBrands(response.Payload)
            .Where(b => NormalizeForSearch(b.Name).Contains(needle, StringComparison.Ordinal))
            .Take(size)
            .ToList();
    }

    /// <summary><c>{ brands: [ { id, name } ] }</c> (ya da düz dizi) → DTO listesi. id/name eksik öğe atlanır.</summary>
    private static List<TrendyolBrandDto> ParseBrands(string payload)
    {
        var result = new List<TrendyolBrandDto>();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return result;
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("brands", out var b) ? b : default;

        if (array.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var el in array.EnumerateArray())
        {
            if (el.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                && idEl.TryGetInt64(out var id)
                && el.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                result.Add(new TrendyolBrandDto { BrandId = id, Name = nameEl.GetString() ?? string.Empty });
            }
        }

        return result;
    }

    /// <summary>Arama-normalize: Türkçe aksanları ASCII tabanına indirger + küçük harfe çevirir (İ/ı/i tuzağını
    /// char-map ile atlar) → aksan/case-duyarsız eşleşme (Trendyol kategori/N11 arama deseniyle aynı).</summary>
    private static string NormalizeForSearch(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Trim())
        {
            sb.Append(ch switch
            {
                'ı' or 'I' or 'İ' or 'i' or 'î' or 'Î' => 'i',
                'ü' or 'Ü' or 'u' or 'U' or 'û' or 'Û' => 'u',
                'ö' or 'Ö' or 'o' or 'O' => 'o',
                'ç' or 'Ç' or 'c' or 'C' => 'c',
                'ş' or 'Ş' or 's' or 'S' => 's',
                'ğ' or 'Ğ' or 'g' or 'G' => 'g',
                'â' or 'Â' or 'a' or 'A' => 'a',
                _ => char.ToLowerInvariant(ch),
            });
        }

        return sb.ToString();
    }
}
