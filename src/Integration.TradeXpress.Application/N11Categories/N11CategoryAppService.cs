using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// N11 kategori taksonomisi AppService — host-global ağaç sync/okuma + on-demand attribute. Ağaç HOST kimliğiyle
/// (config <c>N11:CategorySync:*</c>) bir kez çekilir; attribute'lar çalışılan şirketin N11 kanalının KENDİ
/// AppKey/AppSecret'ıyla (server entity'den okur — client sırrı görmez). REST primary, SOAP fallback client'ta.
/// </summary>
public class N11CategoryAppService : TradeXpressAppService, IN11CategoryAppService
{
    private readonly IRepository<N11Category, Guid> _repository;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IN11CategoryClient _client;
    private readonly IConfiguration _configuration;
    private readonly ICurrentCompany _currentCompany;

    public N11CategoryAppService(
        IRepository<N11Category, Guid> repository,
        IRepository<SalesChannelTrN11, Guid> channelRepository,
        IN11CategoryClient client,
        IConfiguration configuration,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _channelRepository = channelRepository;
        _client = client;
        _configuration = configuration;
        _currentCompany = currentCompany;
    }

    public virtual async Task<int> SyncCategoriesAsync()
    {
        // Host-only: global taksonomiyi yalnız host günceller (CurrentTenant null). Tenant'lar okur.
        if (CurrentTenant.Id is not null)
        {
            throw new BusinessException("TradeXpress:N11:CategorySyncHostOnly");
        }

        var appKey = _configuration["N11:CategorySync:AppKey"];
        var appSecret = _configuration["N11:CategorySync:AppSecret"];
        if (string.IsNullOrWhiteSpace(appKey) || string.IsNullOrWhiteSpace(appSecret))
        {
            throw new BusinessException("TradeXpress:N11:CategorySyncCredentialsMissing");
        }

        var nodes = await _client.GetCategoryTreeAsync(appKey, appSecret);

        var existing = (await _repository.GetListAsync()).ToDictionary(x => x.ExternalId, StringComparer.Ordinal);
        var toInsert = new List<N11Category>();
        var toUpdate = new List<N11Category>();

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
                toInsert.Add(new N11Category(node.ExternalId, node.ParentExternalId, node.Name, node.IsLeaf, node.LastModifiedExternal));
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

    public virtual async Task<List<N11CategoryTreeNodeDto>> GetChildrenAsync(string? parentExternalId)
    {
        var normalized = string.IsNullOrWhiteSpace(parentExternalId) ? null : parentExternalId.Trim();
        // Host-global okuma → host'a sabitle (db-per-tenant'a karşı merkezilik garantisi).
        using (CurrentTenant.Change(null))
        {
            var query = (await _repository.GetQueryableAsync())
                .Where(x => x.ParentExternalId == normalized)
                .OrderBy(x => x.Name);
            var items = await AsyncExecuter.ToListAsync(query);
            return items.Select(x => ObjectMapper.Map<N11Category, N11CategoryTreeNodeDto>(x)).ToList();
        }
    }

    public virtual async Task<List<N11LeafCategoryDto>> GetLeafCategoriesAsync()
    {
        // Host-global ağacı TEK seferde çek → id map → her yaprak için kökten yola in-memory kurulur (yaprak adları
        // çok tekrar ettiğinden yol ayırt eder). ~4400 satır; tek indeksli sorgu + sözlük yürüme (hızlı).
        var all = await AsyncExecuter.ToListAsync(await _repository.GetQueryableAsync());
        var byExternalId = all.ToDictionary(c => c.ExternalId);

        return all
            .Where(c => c.IsLeaf)
            .Select(leaf => new N11LeafCategoryDto { ExternalId = leaf.ExternalId, Path = BuildPath(leaf, byExternalId) })
            .OrderBy(x => x.Path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Yaprağın kökten tam yolu ("A &gt; B &gt; C") — parent zinciri id map'ten yürünür (döngü guard'lı).</summary>
    private static string BuildPath(N11Category leaf, Dictionary<string, N11Category> byExternalId)
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

    public virtual async Task<List<N11CategoryAttributeDto>> GetLeafAttributesAsync(string categoryExternalId)
    {
        var (appKey, appSecret) = await ResolveCurrentCompanyN11CredentialsAsync();
        var leaf = await _client.GetLeafAttributesAsync(categoryExternalId, appKey, appSecret);

        // Client kayıtları → DTO (entity değil → inline map serbest).
        return leaf.Attributes.Select(a => new N11CategoryAttributeDto
        {
            AttributeId = a.AttributeId,
            Name = a.Name,
            IsMandatory = a.IsMandatory,
            IsVariant = a.IsVariant,
            IsCustomValue = a.IsCustomValue,
            Priority = a.Priority,
            Values = a.Values.Select(v => new N11CategoryAttributeValueDto { ValueId = v.ValueId, Value = v.Value }).ToList(),
        }).ToList();
    }

    /// <summary>Sync upsert: değişen alanları uygular; değişiklik olduysa true.</summary>
    private static bool ApplyChanges(N11Category entity, N11CategoryNode node)
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

        if (node.LastModifiedExternal is not null && entity.LastModifiedExternal != node.LastModifiedExternal)
        {
            entity.SetLastModifiedExternal(node.LastModifiedExternal);
            changed = true;
        }

        return changed;
    }

    /// <summary>Çalışılan şirketin N11 kanalının KENDİ AppKey/AppSecret'ı (server entity'den okur; client sırrı görmez).</summary>
    private async Task<(string AppKey, string AppSecret)> ResolveCurrentCompanyN11CredentialsAsync()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:SalesChannel:CompanyRequired");
        }

        var channel = await AsyncExecuter.FirstOrDefaultAsync(
            (await _channelRepository.GetQueryableAsync()).Where(x => x.CompanyId == companyId));
        if (channel is null)
        {
            throw new BusinessException("TradeXpress:N11:NoChannelForCompany");
        }

        return (channel.AppKey, channel.AppSecret);
    }
}
