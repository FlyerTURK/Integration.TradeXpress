using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.EtsyTaxonomies;

/// <summary>
/// Etsy seller taxonomy AppService — host-global ağaç okuma + kullanıcı-tetikli sync + ON-DEMAND property. Ağaç bir Etsy
/// kanalının kimliğiyle (<c>{keystring}:{secret}</c>, app-level x-api-key) çekilir; getSellerTaxonomyNodes Bearer
/// GEREKTİRMEZ → herhangi bir Etsy kanalı yeter. Sync/reconcile mantığı <see cref="EtsyTaxonomySyncManager"/>'da (worker
/// + açılış aynı SSOT'u kullanır; bu AppService yalnız kullanıcı yüzü + yetki). Yetki: kanal ailesiyle AYNI sınır
/// (<c>SalesChannels.Default</c>).
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class EtsyTaxonomyAppService : TradeXpressAppService, IEtsyTaxonomyAppService
{
    private readonly IRepository<EtsyTaxonomy, Guid> _repository;
    private readonly IEtsyTaxonomyClient _client;
    private readonly EtsyTaxonomySyncManager _syncManager;
    private readonly IDistributedCache<EtsyTaxonomyProperties> _propertiesCache;

    public EtsyTaxonomyAppService(
        IRepository<EtsyTaxonomy, Guid> repository,
        IEtsyTaxonomyClient client,
        EtsyTaxonomySyncManager syncManager,
        IDistributedCache<EtsyTaxonomyProperties> propertiesCache)
    {
        _repository = repository;
        _client = client;
        _syncManager = syncManager;
        _propertiesCache = propertiesCache;
    }

    // Sync sınıf-seviyesi Default'ta kalır (Update DEĞİL): taksonomi reconcile idempotent + dış kaynaktan birebir
    // (fiyatlama etkilemez) — picker ilk kullanımda otomatik sync tetiklerse salt-görüntüleyeni kırmamak için.
    // Üç-yönlü reconcile (ekle/güncelle/HARD-sil) manager'da; worker/açılış ile TEK ortak yol.
    public virtual async Task<int> SyncTaxonomyAsync()
    {
        return await _syncManager.ReconcileTaxonomyAsync();
    }

    public virtual async Task<List<EtsyTaxonomyTreeNodeDto>> GetChildrenAsync(string? parentExternalId)
    {
        var normalized = string.IsNullOrWhiteSpace(parentExternalId) ? null : parentExternalId.Trim();
        // Host-global okuma → host'a sabitle (db-per-tenant'a karşı merkezilik garantisi).
        using (CurrentTenant.Change(null))
        {
            var query = (await _repository.GetQueryableAsync())
                .Where(x => x.ParentExternalId == normalized)
                .OrderBy(x => x.Name);
            var items = await AsyncExecuter.ToListAsync(query);
            return items.Select(x => ObjectMapper.Map<EtsyTaxonomy, EtsyTaxonomyTreeNodeDto>(x)).ToList();
        }
    }

    public virtual async Task<List<EtsyLeafCategoryDto>> SearchLeafCategoriesAsync(string term)
    {
        // SERVER-SIDE arama: kullanıcı yazınca çağrılır. En az 3 harf; aksi halde boş (istemciye liste dökülmez).
        var normalizedTerm = NormalizeForSearch(term);
        if (normalizedTerm.Length < 3)
        {
            return new List<EtsyLeafCategoryDto>();
        }

        // Host-global okuma → host'a sabitle (GetChildrenAsync ile aynı merkezilik).
        using (CurrentTenant.Change(null))
        {
            // Ağaç → id map → yaprak TAM yolları; tek sorgu + sözlük yürüme.
            var all = await AsyncExecuter.ToListAsync(await _repository.GetQueryableAsync());
            var byExternalId = all.ToDictionary(c => c.ExternalId);

            return all
                .Where(c => c.IsLeaf)
                .Select(leaf => new EtsyLeafCategoryDto { ExternalId = leaf.ExternalId, FullPathName = BuildPath(leaf, byExternalId) })
                .Where(x => NormalizeForSearch(x.FullPathName).Contains(normalizedTerm, StringComparison.Ordinal))
                .OrderBy(x => x.FullPathName, StringComparer.CurrentCultureIgnoreCase)
                .Take(50)   // en fazla 50 sonuç (picker grid'i); daha fazlası için kullanıcı aramayı daraltır
                .ToList();
        }
    }

    public virtual async Task<EtsyLeafCategoryDto?> GetByExternalIdAsync(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        var normalized = externalId.Trim();
        var paths = await GetPathsAsync(new[] { normalized });
        return paths.TryGetValue(normalized, out var fullPath)
            ? new EtsyLeafCategoryDto { ExternalId = normalized, FullPathName = fullPath }
            : null;   // tabloda yok → bayat; çağıran "yeniden seç" işaretini kurar (asla throw)
    }

    public virtual async Task<Dictionary<string, string>> GetPathsAsync(IEnumerable<string> externalIds)
    {
        var wanted = externalIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        // Host-global okuma → host'a sabitle (GetChildren/Search ile aynı merkezilik). Tam yol için parent zinciri
        // gerektiğinden ağacın tamamı bir kez yüklenir (SearchLeafCategoriesAsync ile aynı desen; ~3065 satır).
        using (CurrentTenant.Change(null))
        {
            var all = await AsyncExecuter.ToListAsync(await _repository.GetQueryableAsync());
            var byExternalId = all.ToDictionary(c => c.ExternalId);

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var id in wanted)
            {
                if (byExternalId.TryGetValue(id, out var node))
                {
                    result[id] = BuildPath(node, byExternalId);
                }
            }

            return result;
        }
    }

    /// <summary>Arama-normalize — aksan/case-duyarsız eşleşme (İngilizce baskın ama Türkçe terimlere de tolerans).</summary>
    private static string NormalizeForSearch(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim().ToLowerInvariant();
    }

    /// <summary>Yaprağın kökten tam yolu ("A &gt; B &gt; C") — parent zinciri id map'ten yürünür (döngü guard'lı).</summary>
    private static string BuildPath(EtsyTaxonomy leaf, Dictionary<string, EtsyTaxonomy> byExternalId)
    {
        var parts = new List<string>();
        var current = leaf;
        var guard = 0;
        while (current is not null && guard++ < 20)
        {
            parts.Add(current.Name);
            current = current.ParentExternalId is { } parentId && byExternalId.TryGetValue(parentId, out var parent)
                ? parent
                : null;
        }

        parts.Reverse();
        return string.Join(" > ", parts);
    }

    public virtual async Task<List<EtsyTaxonomyPropertyDto>> GetPropertiesAsync(long taxonomyId)
    {
        var properties = await GetPropertiesCachedAsync(taxonomyId);

        // Client kayıtları → DTO (entity değil → inline map serbest; N11/Trendyol attribute deseniyle aynı). Jenerik
        // property'ler (PossibleValues boş) ELENMEZ — hepsi döner; UI IsRequired/SupportsVariations ile sıralar/filtreler.
        return properties.Properties.Select(p => new EtsyTaxonomyPropertyDto
        {
            PropertyId = p.PropertyId,
            Name = p.Name,
            DisplayName = p.DisplayName,
            IsRequired = p.IsRequired,
            SupportsVariations = p.SupportsVariations,
            IsMultivalued = p.IsMultivalued,
            MaxValuesAllowed = p.MaxValuesAllowed,
            PossibleValues = p.PossibleValues
                .Select(v => new EtsyTaxonomyPropertyValueDto { ValueId = v.ValueId, Name = v.Name })
                .ToList(),
        }).ToList();
    }

    /// <summary>Property tanımını ON-DEMAND çeker + 6 saat dağıtık cache'ler (Trendyol <c>GetLeafAttributesCachedAsync</c>
    /// deseni; tanımlar nadiren değişir, her seçimde Etsy'ye gitmeye gerek yok — KALICI TABLO YOK, yalnız cache). Kimlik
    /// yalnız cache-miss'te çözülür (manager = SSOT). Alınamazsa fail-fast (dostane BusinessException).</summary>
    private async Task<EtsyTaxonomyProperties> GetPropertiesCachedAsync(long taxonomyId)
    {
        try
        {
            return (await _propertiesCache.GetOrAddAsync(
                $"EtsyTaxonomyProperties:{taxonomyId}",
                async () =>
                {
                    var apiKeyHeader = await _syncManager.ResolveEtsyApiKeyHeaderAsync();
                    var results = await _client.GetPropertiesByTaxonomyIdAsync(apiKeyHeader, taxonomyId);
                    return new EtsyTaxonomyProperties(taxonomyId, results);
                },
                () => new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6),
                }))!;
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Etsy taksonomi property tanımı alınamadı ({TaxonomyId}).", taxonomyId);
            throw new BusinessException("TradeXpress:Etsy:Taxonomy:PropertiesUnavailable")
                .WithData("TaxonomyId", taxonomyId);
        }
    }
}
