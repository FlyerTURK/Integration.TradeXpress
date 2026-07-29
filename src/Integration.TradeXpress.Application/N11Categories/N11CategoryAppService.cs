using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// N11 kategori taksonomisi AppService — host-global ağaç sync/okuma + on-demand attribute. Ağaç HOST kimliğiyle
/// (config <c>N11:CategorySync:*</c>) bir kez çekilir; attribute'lar çalışılan şirketin N11 kanalının KENDİ
/// AppKey/AppSecret'ıyla (server entity'den okur — client sırrı görmez). REST primary, SOAP fallback client'ta.
/// Yetki: kanal ailesiyle AYNI sınır (SalesChannels.*) — fiyatlamayı değiştiren komisyon import'u ayrıca Update
/// ister; "host-only" tenant kontrolü TEK BAŞINA yetki DEĞİLDİR (inceleme bulgusu).
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class N11CategoryAppService : TradeXpressAppService, IN11CategoryAppService
{
    private readonly IRepository<N11Category, Guid> _repository;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IN11CategoryClient _client;
    private readonly ICurrentCompany _currentCompany;
    private readonly N11CategorySyncManager _syncManager;

    public N11CategoryAppService(
        IRepository<N11Category, Guid> repository,
        IRepository<SalesChannelTrN11, Guid> channelRepository,
        IN11CategoryClient client,
        ICurrentCompany currentCompany,
        N11CategorySyncManager syncManager)
    {
        _repository = repository;
        _channelRepository = channelRepository;
        _client = client;
        _currentCompany = currentCompany;
        _syncManager = syncManager;
    }

    // Sync sınıf-seviyesi Default'ta kalır (Update DEĞİL): taksonomi upsert'i idempotent + dış kaynaktan birebir,
    // salt-görüntüleyen kullanıcıyı kırmamak için dar tutuluyor. Uç, kanal kurulum akışının (N11ChannelProvisioner)
    // ihtiyacı için DURUYOR; rutin tazeleme artık N11CategorySyncWorker'ın işi — kullanıcı hiçbir şeye basmaz.
    public virtual async Task<int> SyncCategoriesAsync()
    {
        return await _syncManager.ReconcileAsync();
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

    public virtual async Task<List<N11LeafCategoryDto>> SearchLeafCategoriesAsync(string term)
    {
        // SERVER-SIDE arama: kullanıcı yazınca çağrılır. En az 3 harf; aksi halde boş (istemciye liste dökülmez).
        // Türkçe aksan/DÖNÜŞÜMSÜZ eşleşme: "kul" → "Kül" bulur (NormalizeForSearch ile iki taraf da ASCII-lower).
        var normalizedTerm = NormalizeForSearch(term);
        if (normalizedTerm.Length < 3)
        {
            return new List<N11LeafCategoryDto>();
        }

        // Host-global okuma → host'a sabitle (db-per-tenant merkeziliği; GetChildrenAsync ile aynı).
        using (CurrentTenant.Change(null))
        {
            // Ağaç → id map → yaprak TAM yolları (kökten mega dahil). ~4400 satır; tek sorgu + sözlük yürüme.
            var all = await AsyncExecuter.ToListAsync(await _repository.GetQueryableAsync());
            var byExternalId = all.ToDictionary(c => c.ExternalId);

            return all
                .Where(c => c.IsLeaf)
                .Select(leaf => new N11LeafCategoryDto { ExternalId = leaf.ExternalId, Path = BuildPath(leaf, byExternalId) })
                .Where(x => NormalizeForSearch(x.Path).Contains(normalizedTerm, StringComparison.Ordinal))
                .OrderBy(x => x.Path, StringComparer.CurrentCultureIgnoreCase)
                .Take(50)   // en fazla 50 sonuç (picker grid'i); daha fazlası için kullanıcı aramayı daraltır
                .ToList();
        }
    }

    /// <summary>Arama-normalize — merkezi <see cref="N11NameNormalizer"/> (komisyon TSV eşlemesiyle AYNI kural).</summary>
    private static string NormalizeForSearch(string? text)
    {
        return N11NameNormalizer.Normalize(text);
    }

    // Komisyon içe aktarma UCU 2026-07-28'de KALDIRILDI: komisyon oranları N11'de kanala özel değil, kategoriye
    // aittir — kullanıcıya kanal ayarlarında bir düğme sunmak yanlış yerdeydi. Uygulama artık kategori
    // mutabakatının parçası (N11CategorySyncManager), günde bir kez kendiliğinden çalışır.

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
