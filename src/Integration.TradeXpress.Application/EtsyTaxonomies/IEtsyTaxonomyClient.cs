using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.EtsyTaxonomies;

/// <summary>
/// Etsy seller taxonomy API istemcisi (server-side infra) — REST <c>/application/seller-taxonomy/nodes</c> tüm
/// ağacı iç içe <c>children[]</c> ile tek çağrıda verir; burada FLAT normalize sözleşmeye indirilir. Bu uç
/// app-level'dır: yalnız <c>x-api-key</c> ister, Bearer token GEREKTİRMEZ (satıcı-onaylı erişim yok). Çağıran
/// kimliği (<c>{keystring}:{secret}</c>) parametre geçer — istemci sırrı SAKLAMAZ.
/// </summary>
public interface IEtsyTaxonomyClient
{
    /// <summary>Tüm seller taxonomy ağacını FLAT liste olarak çeker (REST <c>seller-taxonomy/nodes</c>; iç içe
    /// <c>children[]</c> özyinelemeli düzleştirilir). <paramref name="apiKeyHeader"/> = <c>{keystring}:{secret}</c>.</summary>
    Task<IReadOnlyList<EtsyTaxonomyNode>> GetSellerTaxonomyNodesAsync(
        string apiKeyHeader, CancellationToken cancellationToken = default);

    /// <summary>Bir taksonomi düğümünün property (attribute) tanımlarını ON-DEMAND çeker
    /// (<c>GET /application/seller-taxonomy/nodes/{taxonomyId}/properties</c>). Bu uç da app-level'dır: yalnız
    /// <c>x-api-key</c> ister, Bearer token GEREKTİRMEZ (canlı doğrulandı). Jenerik property'ler (possible_values boş
    /// olanlar dahil) ELENMEZ — hepsi döner (UI ileride filtreler). <paramref name="apiKeyHeader"/> = <c>{keystring}:{secret}</c>.</summary>
    Task<IReadOnlyList<EtsyTaxonomyPropertyResult>> GetPropertiesByTaxonomyIdAsync(
        string apiKeyHeader, long taxonomyId, CancellationToken cancellationToken = default);
}

/// <summary>Normalize taxonomy düğümü (REST tree'den düzleştirilmiş). Id-only ağaç: parent id ile bağlanır.
/// Etsy <c>children[]</c> boşsa <see cref="IsLeaf"/>=true.</summary>
public sealed record EtsyTaxonomyNode(
    string ExternalId, string? ParentExternalId, string Name, bool IsLeaf, int Level);

/// <summary>Bir taksonomi düğümünün property (attribute) tanımı — ON-DEMAND çekilir, KALICI SAKLANMAZ (yalnız cache).
/// <see cref="IsRequired"/>=zorunlu; <see cref="SupportsVariations"/>=varyant ekseni olabilir; <see cref="IsMultivalued"/>=
/// çoklu değer; <see cref="MaxValuesAllowed"/>=izinli maksimum değer (yoksa null). <see cref="PossibleValues"/> boş =
/// jenerik/serbest property (UI filtreler; burada ELENMEZ).</summary>
public sealed record EtsyTaxonomyPropertyResult(
    long PropertyId,
    string Name,
    string DisplayName,
    bool IsRequired,
    bool SupportsVariations,
    bool IsMultivalued,
    int? MaxValuesAllowed,
    IReadOnlyList<EtsyTaxonomyPropertyValue> PossibleValues);

/// <summary>Property için önceden tanımlı değer — id-bazlı ({value_id, name}).</summary>
public sealed record EtsyTaxonomyPropertyValue(long ValueId, string Name);

/// <summary>Bir taksonomi düğümünün property setinin cache sarmalayıcısı (Trendyol <c>TrendyolLeafAttributes</c> ikizi) —
/// KALICI TABLO YOK, yalnız <c>IDistributedCache</c>'te tutulur.</summary>
public sealed record EtsyTaxonomyProperties(
    long TaxonomyId, IReadOnlyList<EtsyTaxonomyPropertyResult> Properties);
