using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Trendyol;

namespace Integration.TradeXpress.TrendyolBrands;

/// <summary>
/// Trendyol marka arama istemcisi (server-side infra) — REST/JSON, yalnız GET (pazaryerine SIFIR yazma).
/// Ada göre arama SSOT'tur; marka verisi saklanmaz. Auth (Basic + zorunlu <c>User-Agent</c>) taban
/// (<see cref="TrendyolRestClientBase"/>) tarafından eklenir. Sınıf adı arayüzle eşleştiğinden ABP otomatik expose eder.
/// </summary>
public interface ITrendyolBrandClient
{
    /// <summary>Markayı ada göre aratır: önce <c>GET brands/by-name?name=</c>; endpoint yoksa (404) ya da boşsa
    /// <c>GET brands?page=0&amp;size=</c> üzerinden aksan/case-duyarsız contains-filtre fallback (best-effort — sayfalı
    /// pencere alfabetik ilk N marka olduğundan tam tarama değildir). En fazla <paramref name="size"/> sonuç.</summary>
    Task<List<TrendyolBrandDto>> SearchByNameAsync(
        TrendyolCredentials credentials, string name, int size = 20, CancellationToken cancellationToken = default);
}
