using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Trendyol;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.TrendyolCategories;

/// <summary>
/// Trendyol kategori taksonomisi AppService — host-global ağaç sync/okuma. Ağaç, çalışılan şirketin Trendyol kanalının
/// KENDİ kimliğiyle çekilir (kategori endpoint'i public ama zorunlu <c>User-Agent</c> için SellerId lazım; kimlik
/// per-kanal çözülür — eleştiri F-kimlik) ve HOST-GLOBAL tabloya yazılır (tüm tenant'lar paylaşır). Okuma host'a
/// sabitlenir (<c>CurrentTenant.Change(null)</c>). N11 kategori AppService deseniyle simetrik; komisyon/mega katmanı yok.
/// Yetki: kanal ailesiyle AYNI sınır (SalesChannels.*) — kimliksiz/izinsiz erişime kapalı (N11 simetriği;
/// inceleme bulgusu).
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class TrendyolCategoryAppService : TradeXpressAppService, ITrendyolCategoryAppService
{
    private readonly IRepository<TrendyolCategory, Guid> _repository;
    private readonly ITrendyolCategoryClient _client;
    private readonly ITrendyolCredentialResolver _credentialResolver;
    private readonly IDistributedCache<TrendyolLeafAttributes> _leafAttributeCache;

    public TrendyolCategoryAppService(
        IRepository<TrendyolCategory, Guid> repository,
        ITrendyolCategoryClient client,
        ITrendyolCredentialResolver credentialResolver,
        IDistributedCache<TrendyolLeafAttributes> leafAttributeCache)
    {
        _repository = repository;
        _client = client;
        _credentialResolver = credentialResolver;
        _leafAttributeCache = leafAttributeCache;
    }

    // Sync sınıf-seviyesi Default'ta kalır (Update DEĞİL): kategori picker ilk kullanımda otomatik sync tetikler
    // (DB boşsa; TrendyolCategoryPicker) — salt-görüntüleyen kullanıcıyı kırmamak için. Taksonomi upsert'i
    // idempotent + dış kaynaktan birebir (N11 simetriği).
    public virtual async Task<int> SyncCategoriesAsync()
    {
        // Kimlik önce çözülür (company bağlamında — SellerId zorunlu User-Agent için); yazım sonra host'a sabitlenir.
        var credentials = await _credentialResolver.ResolveForCurrentCompanyAsync();
        var nodes = await _client.GetCategoryTreeAsync(credentials);

        // Host-global upsert → host'a sabitle (db-per-tenant'a karşı merkezilik garantisi; N11 okuma deseniyle aynı).
        using (CurrentTenant.Change(null))
        {
            var existing = (await _repository.GetListAsync()).ToDictionary(x => x.ExternalId, StringComparer.Ordinal);
            var toInsert = new List<TrendyolCategory>();
            var toUpdate = new List<TrendyolCategory>();

            foreach (var node in nodes)
            {
                if (existing.TryGetValue(node.ExternalId, out var entity))
                {
                    if (ApplyChanges(entity, node))
                    {
                        toUpdate.Add(entity);
                    }
                }
                else
                {
                    toInsert.Add(new TrendyolCategory(node.ExternalId, node.ParentExternalId, node.Name, node.IsLeaf));
                }
            }

            if (toInsert.Count > 0)
            {
                await _repository.InsertManyAsync(toInsert, autoSave: true);
            }

            if (toUpdate.Count > 0)
            {
                await _repository.UpdateManyAsync(toUpdate, autoSave: true);
            }

            return toInsert.Count + toUpdate.Count;
        }
    }

    public virtual async Task<List<TrendyolCategoryTreeNodeDto>> GetChildrenAsync(string? parentExternalId)
    {
        var normalized = string.IsNullOrWhiteSpace(parentExternalId) ? null : parentExternalId.Trim();
        // Host-global okuma → host'a sabitle (db-per-tenant merkeziliği).
        using (CurrentTenant.Change(null))
        {
            var query = (await _repository.GetQueryableAsync())
                .Where(x => x.ParentExternalId == normalized)
                .OrderBy(x => x.Name);
            var items = await AsyncExecuter.ToListAsync(query);
            return items.Select(x => ObjectMapper.Map<TrendyolCategory, TrendyolCategoryTreeNodeDto>(x)).ToList();
        }
    }

    public virtual async Task<List<TrendyolLeafCategoryDto>> SearchLeafCategoriesAsync(string term)
    {
        // SERVER-SIDE arama: en az 3 harf; aksi halde boş (istemciye liste dökülmez). Türkçe aksan/case-duyarsız.
        var normalizedTerm = NormalizeForSearch(term);
        if (normalizedTerm.Length < 3)
        {
            return new List<TrendyolLeafCategoryDto>();
        }

        using (CurrentTenant.Change(null))
        {
            // Ağaç → id map → yaprak TAM yolları; tek sorgu + sözlük yürüme (N11 arama deseniyle aynı).
            var all = await AsyncExecuter.ToListAsync(await _repository.GetQueryableAsync());
            var byExternalId = all.ToDictionary(c => c.ExternalId, StringComparer.Ordinal);

            return all
                .Where(c => c.IsLeaf)
                .Select(leaf => new TrendyolLeafCategoryDto { ExternalId = leaf.ExternalId, Path = BuildPath(leaf, byExternalId) })
                .Where(x => NormalizeForSearch(x.Path).Contains(normalizedTerm, StringComparison.Ordinal))
                .OrderBy(x => x.Path, StringComparer.CurrentCultureIgnoreCase)
                .Take(50)
                .ToList();
        }
    }

    public virtual async Task<List<TrendyolLeafAttributeDto>> GetLeafAttributesAsync(string categoryExternalId)
    {
        var leaf = await GetLeafAttributesCachedAsync(categoryExternalId);

        // Client kayıtları → DTO (entity değil → inline map serbest). Varianter filtreleme UI'ya bırakılır (SKU seviyesi).
        return leaf.Attributes.Select(a => new TrendyolLeafAttributeDto
        {
            AttributeId = a.AttributeId,
            Name = a.Name,
            Required = a.Required,
            Varianter = a.Varianter,
            AllowCustom = a.AllowCustom,
            Values = a.Values.Select(v => new TrendyolAttributeValueDto { ValueId = v.ValueId, Value = v.Value }).ToList(),
        }).ToList();
    }

    /// <summary>Yaprak attribute tanımını 6 saat dağıtık cache'ler (N11 <c>GetLeafAttributesCachedAsync</c> deseni; tanımlar
    /// nadiren değişir, her seçimde Trendyol'a gitmeye gerek yok). Kimlik yalnız cache-miss'te çözülür. Alınamazsa fail-fast.</summary>
    private async Task<TrendyolLeafAttributes> GetLeafAttributesCachedAsync(string categoryExternalId)
    {
        try
        {
            return (await _leafAttributeCache.GetOrAddAsync(
                $"TrendyolLeafAttributes:{categoryExternalId}",
                async () =>
                {
                    var credentials = await _credentialResolver.ResolveForCurrentCompanyAsync();
                    return await _client.GetLeafAttributesAsync(credentials, categoryExternalId);
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
            Logger.LogWarning(ex, "Trendyol kategori attribute tanımı alınamadı ({CategoryId}).", categoryExternalId);
            throw new BusinessException("TradeXpress:Trendyol:Category:AttributesUnavailable")
                .WithData("CategoryId", categoryExternalId);
        }
    }

    /// <summary>Sync upsert: değişen alanları uygular; değişiklik olduysa true.</summary>
    private static bool ApplyChanges(TrendyolCategory entity, TrendyolCategoryNode node)
    {
        var changed = false;
        if (!string.Equals(entity.Name, node.Name, StringComparison.Ordinal))
        {
            entity.SetName(node.Name);
            changed = true;
        }

        if (!string.Equals(entity.ParentExternalId, node.ParentExternalId, StringComparison.Ordinal))
        {
            entity.SetParent(node.ParentExternalId);
            changed = true;
        }

        if (entity.IsLeaf != node.IsLeaf)
        {
            entity.SetIsLeaf(node.IsLeaf);
            changed = true;
        }

        return changed;
    }

    /// <summary>Arama-normalize: Türkçe aksanları ASCII tabanına indirger + küçük harfe çevirir (İ/ı/i tuzağını
    /// char-map ile atlar). "Kül"→"kul", "kul"→"kul" → aksan/case-duyarsız eşleşme (N11 ile aynı).</summary>
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

    /// <summary>Yaprağın kökten tam yolu ("A &gt; B &gt; C") — parent zinciri id map'ten yürünür (döngü guard'lı).</summary>
    private static string BuildPath(TrendyolCategory leaf, Dictionary<string, TrendyolCategory> byExternalId)
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
}
