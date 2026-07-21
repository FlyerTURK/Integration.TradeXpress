using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.EtsyTaxonomies;

/// <summary>
/// Etsy seller taxonomy — host-global ağaç. Ağaç bir Etsy kanalının kimliğiyle (app-level x-api-key) bir kez
/// sync'lenir (tüm tenant'lar paylaşır — N11 kategori ikizi). getSellerTaxonomyNodes Bearer token GEREKTİRMEZ;
/// yalnız <c>x-api-key = {keystring}:{secret}</c> ister → herhangi bir Etsy kanalının kimliği yeter.
/// </summary>
public interface IEtsyTaxonomyAppService : IApplicationService
{
    /// <summary>Host-only: REST'ten tüm taxonomy ağacını çekip <c>EtsyTaxonomy</c>'ye upsert eder. Eklenen+güncellenen sayısını döner.</summary>
    Task<int> SyncTaxonomyAsync();

    /// <summary>Ağaç gezinme — verilen üst düğümün çocukları (null → kökler). Host-global veriden okunur.</summary>
    Task<List<EtsyTaxonomyTreeNodeDto>> GetChildrenAsync(string? parentExternalId);

    /// <summary>Yaprak kategori SERVER-SIDE arama (LookupEdit): en az 3 harf. Tam yol adıyla döner (yaprak adları
    /// tekrar ettiğinden), en fazla 50 sonuç. &lt;3 harf → boş.</summary>
    Task<List<EtsyLeafCategoryDto>> SearchLeafCategoriesAsync(string term);

    /// <summary>Bir taksonomi düğümünü DIŞ ID ile çözer (ExternalId + tam yol adı). Kanal-ürünün sakladığı bayat
    /// <c>TaxonomyId</c>'yi okuma anında ada çevirmek için (KALICI ad saklanmaz). Tabloda YOKSA (reconcile sildi/değişti)
    /// <c>null</c> döner — ASLA throw etmez ("bayat kategori" işareti çağıranda kurulur).</summary>
    Task<EtsyLeafCategoryDto?> GetByExternalIdAsync(string externalId);

    /// <summary>Toplu dış-id → tam yol adı çözümü (birden çok kanal-ürünün taksonomilerini TEK sorguda çözmek için).
    /// Yalnızca tabloda BULUNAN id'ler sözlükte yer alır (bulunamayan = bayat, çağıran eksikliği "stale" yorumlar).
    /// Boş/whitespace id'ler atlanır. ASLA throw etmez.</summary>
    Task<Dictionary<string, string>> GetPathsAsync(IEnumerable<string> externalIds);

    /// <summary>Bir taksonomi düğümünün property (attribute) tanımlarını ON-DEMAND getirir (API'den çekilir, KALICI
    /// TABLO YOK; ~6 saat dağıtık cache). Jenerik property'ler de dahil döner (UI filtreler). Kanal yoksa dostane hata.</summary>
    Task<List<EtsyTaxonomyPropertyDto>> GetPropertiesAsync(long taxonomyId);
}
