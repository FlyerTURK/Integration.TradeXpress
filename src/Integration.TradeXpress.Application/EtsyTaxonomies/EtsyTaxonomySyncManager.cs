using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.EtsyTaxonomies;

/// <summary>
/// Etsy seller taxonomy TAM-RECONCILE + bayatlık kapısı — worker/açılış HOST bağlamında çağırır (bu yüzden AppService
/// DEĞİL: <c>[Authorize]</c> interceptor'ı kullanıcısız worker'da patlardı; <see cref="Orders.OrderSyncManager"/> ikizi).
/// Katalog HOST-GLOBAL (<c>CurrentTenant.Change(null)</c>); kanal ise tenant-owned → kimlik <see cref="IDataFilter"/> ile
/// <c>IMultiTenant</c> filtresi DISABLE edilerek bulunur (Etsy app keystring'i tüm kanallarda AYNI → herhangi biri yeter).
/// UoW yönetimi TAMAMEN burada (worker'da ambient UoW yok): kısa read UoW → HTTP (DbContext'i tutmadan) → write UoW.
/// </summary>
public class EtsyTaxonomySyncManager : DomainService
{
    /// <summary>Config yoksa varsayılan sync eşiği/periyodu (saat).</summary>
    private const int DefaultSyncIntervalHours = 24;

    /// <summary>Bayatlık eşiği config anahtarı (<c>Etsy:Taxonomy:SyncIntervalHours</c>).</summary>
    private const string SyncIntervalConfigKey = "Etsy:Taxonomy:SyncIntervalHours";

    private readonly IRepository<EtsyTaxonomy, Guid> _repository;
    private readonly IRepository<SalesChannelEtsy, Guid> _channelRepository;
    private readonly IEtsyTaxonomyClient _client;
    private readonly IDataFilter _dataFilter;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IClock _clock;
    private readonly IConfiguration _configuration;

    public EtsyTaxonomySyncManager(
        IRepository<EtsyTaxonomy, Guid> repository,
        IRepository<SalesChannelEtsy, Guid> channelRepository,
        IEtsyTaxonomyClient client,
        IDataFilter dataFilter,
        IUnitOfWorkManager uowManager,
        IClock clock,
        IConfiguration configuration)
    {
        _repository = repository;
        _channelRepository = channelRepository;
        _client = client;
        _dataFilter = dataFilter;
        _uowManager = uowManager;
        _clock = clock;
        _configuration = configuration;
    }

    /// <summary>Config'ten bayatlık eşiği/worker periyodu (<c>Etsy:Taxonomy:SyncIntervalHours</c>, yoksa/≤0 → 24 saat).</summary>
    public TimeSpan ResolveSyncInterval()
    {
        var hours = _configuration.GetValue<int?>(SyncIntervalConfigKey) ?? DefaultSyncIntervalHours;
        if (hours <= 0)
        {
            hours = DefaultSyncIntervalHours;
        }

        return TimeSpan.FromHours(hours);
    }

    /// <summary>Tablo BOŞ veya en son güncellenme <paramref name="threshold"/>'dan eski ise reconcile çalıştırır (true);
    /// aksi halde atlar (false). En son tarih = <c>MAX(COALESCE(LastModificationTime, CreationTime))</c>; saat UTC (<see cref="IClock"/>).</summary>
    public virtual async Task<bool> SyncIfStaleAsync(TimeSpan threshold, CancellationToken cancellationToken = default)
    {
        bool shouldSync;
        using (CurrentTenant.Change(null))
        using (var uow = _uowManager.Begin(requiresNew: true))
        {
            var query = await _repository.GetQueryableAsync();
            if (!await AsyncExecuter.AnyAsync(query))
            {
                shouldSync = true;
            }
            else
            {
                var maxDate = await AsyncExecuter.MaxAsync(query, x => x.LastModificationTime ?? x.CreationTime);
                shouldSync = (_clock.Now - maxDate) >= threshold;
            }

            await uow.CompleteAsync(cancellationToken);
        }

        if (!shouldSync)
        {
            return false;
        }

        await ReconcileTaxonomyAsync(cancellationToken);
        return true;
    }

    /// <summary>Üç-yönlü tam-reconcile: taze ağacı çeker → var olmayanı EKLE, değişeni GÜNCELLE, taze kümede OLMAYANI
    /// HARD-DELETE (EtsyTaxonomy'ye FK yok; kanal-ürün TaxonomyId'yi düz long tutar → referans bütünlüğü yok, kalıcı sil
    /// temiz). Değişen (ekle+güncelle+sil) toplam sayısını döner. Kanal yoksa dostane <see cref="BusinessException"/>
    /// (worker/açılış tarafı bu istisnayı YUTAR + loglar).</summary>
    public virtual async Task<int> ReconcileTaxonomyAsync(CancellationToken cancellationToken = default)
    {
        // Kimlik önce çözülür (kısa read UoW; kanal tenant-owned → IMultiTenant filtresi disable). Sonra HTTP DbContext'i
        // tutmadan çalışır (taksonomi ~3065 node, 60 sn timeout).
        string apiKeyHeader;
        using (CurrentTenant.Change(null))
        using (var readUow = _uowManager.Begin(requiresNew: true))
        {
            apiKeyHeader = await ResolveEtsyApiKeyHeaderAsync();
            await readUow.CompleteAsync(cancellationToken);
        }

        var nodes = await _client.GetSellerTaxonomyNodesAsync(apiKeyHeader, cancellationToken);
        var freshIds = new HashSet<string>(nodes.Select(n => n.ExternalId), StringComparer.Ordinal);

        using (CurrentTenant.Change(null))
        using (var writeUow = _uowManager.Begin(requiresNew: true))
        {
            var existing = (await _repository.GetListAsync()).ToDictionary(x => x.ExternalId, StringComparer.Ordinal);
            var toInsert = new List<EtsyTaxonomy>();
            var toUpdate = new List<EtsyTaxonomy>();

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
                    toInsert.Add(new EtsyTaxonomy(node.ExternalId, node.ParentExternalId, node.Name, node.IsLeaf, node.Level));
                }
            }

            // SİL: taze ağaçta artık bulunmayan mevcut satırlar → HARD-DELETE (soft-delete DEĞİL: referans bütünlüğü yok,
            // kalıcı temizlik doğru davranış — bayat düğüm birikmesin).
            var toDelete = existing.Values.Where(x => !freshIds.Contains(x.ExternalId)).ToList();

            if (toInsert.Count > 0)
            {
                await _repository.InsertManyAsync(toInsert, autoSave: true);
            }

            if (toUpdate.Count > 0)
            {
                await _repository.UpdateManyAsync(toUpdate, autoSave: true);
            }

            if (toDelete.Count > 0)
            {
                await _repository.HardDeleteAsync(toDelete, autoSave: true);
            }

            await writeUow.CompleteAsync(cancellationToken);

            Logger.LogInformation(
                "Etsy taxonomy reconcile: +{Inserted} eklendi, ~{Updated} güncellendi, -{Deleted} silindi ({Total} node taze).",
                toInsert.Count, toUpdate.Count, toDelete.Count, freshIds.Count);

            return toInsert.Count + toUpdate.Count + toDelete.Count;
        }
    }

    /// <summary>Bir Etsy kanalının kimliği → <c>{keystring}:{secret}</c> (app-level x-api-key). Kanal tenant-owned →
    /// <c>IMultiTenant</c> filtresi DISABLE edilerek bulunur (host bağlamından da görünsün); app keystring'i tüm
    /// kanallarda AYNI → herhangi biri yeter. Kanal yoksa dostane <see cref="BusinessException"/>. Bir UoW içinde çağrılmalı.</summary>
    public virtual async Task<string> ResolveEtsyApiKeyHeaderAsync()
    {
        // Kanal tenant-owned VE company-scoped → host/worker bağlamından görünmesi için İKİ filtre de kapatılır
        // (yalnız IMultiTenant yetmez: company filtresi `TenantId IS NULL OR CompanyId=Current` tenant-owned kanalı eler).
        // OrderSyncManager deseniyle hizalı.
        SalesChannelEtsy? channel;
        using (_dataFilter.Disable<IMultiTenant>())
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            channel = await AsyncExecuter.FirstOrDefaultAsync(await _channelRepository.GetQueryableAsync());
        }

        if (channel is null)
        {
            throw new BusinessException("TradeXpress:Etsy:Taxonomy:NoChannel");
        }

        return $"{channel.Keystring}:{channel.SharedSecret}";
    }

    /// <summary>Sync upsert: değişen alanları uygular; değişiklik olduysa true.</summary>
    private static bool ApplyChanges(EtsyTaxonomy entity, EtsyTaxonomyNode node)
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

        if (entity.Level != node.Level)
        {
            entity.SetLevel(node.Level);
            changed = true;
        }

        return changed;
    }
}
