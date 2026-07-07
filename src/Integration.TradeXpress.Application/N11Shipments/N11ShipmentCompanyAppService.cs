using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// N11 kargo firması AppService — host-global sync/okuma. Sync HOST kimliğiyle (config <c>N11:CategorySync:*</c>,
/// tüm N11 host-sync'leri ortak) + tam re-sync (ekle/güncelle/SİL). Okuma <c>CurrentTenant.Change(null)</c> ile
/// host'a sabitlenir (db-per-tenant'a karşı merkezilik garantisi). Kaynak SOAP.
/// </summary>
public class N11ShipmentCompanyAppService : TradeXpressAppService, IN11ShipmentCompanyAppService
{
    private readonly IRepository<N11ShipmentCompany, Guid> _repository;
    private readonly IN11ShipmentCompanyClient _client;
    private readonly IConfiguration _configuration;

    public N11ShipmentCompanyAppService(
        IRepository<N11ShipmentCompany, Guid> repository,
        IN11ShipmentCompanyClient client,
        IConfiguration configuration)
    {
        _repository = repository;
        _client = client;
        _configuration = configuration;
    }

    public virtual async Task<int> SyncAsync()
    {
        if (CurrentTenant.Id is not null)
        {
            throw new BusinessException("TradeXpress:N11:ShipmentSyncHostOnly");
        }

        var appKey = _configuration["N11:CategorySync:AppKey"];
        var appSecret = _configuration["N11:CategorySync:AppSecret"];
        if (string.IsNullOrWhiteSpace(appKey) || string.IsNullOrWhiteSpace(appSecret))
        {
            throw new BusinessException("TradeXpress:N11:ShipmentSyncCredentialsMissing");
        }

        var companies = await _client.GetShipmentCompaniesAsync(appKey, appSecret);
        var existing = (await _repository.GetListAsync()).ToDictionary(x => x.ExternalId, StringComparer.Ordinal);
        var inserts = new List<N11ShipmentCompany>();
        var updates = new List<N11ShipmentCompany>();
        var fetchedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in companies)
        {
            fetchedIds.Add(c.ExternalId);
            if (existing.TryGetValue(c.ExternalId, out var entity))
            {
                if (!string.Equals(entity.Name, c.Name, StringComparison.Ordinal) ||
                    !string.Equals(entity.ShortName, c.ShortName, StringComparison.Ordinal))
                {
                    entity.SetName(c.Name);
                    entity.SetShortName(c.ShortName);
                    updates.Add(entity);
                }
            }
            else
            {
                inserts.Add(new N11ShipmentCompany(c.ExternalId, c.Name, c.ShortName));
            }
        }

        if (inserts.Count > 0)
        {
            await _repository.InsertManyAsync(inserts, autoSave: true);
        }

        if (updates.Count > 0)
        {
            await _repository.UpdateManyAsync(updates, autoSave: true);
        }

        // Stale temizliği: N11'de artık olmayan firmaları kaldır (GetShipmentCompanies hata verirse üstte throw → buraya ulaşılmaz).
        var stale = existing.Values.Where(x => !fetchedIds.Contains(x.ExternalId)).ToList();
        if (stale.Count > 0)
        {
            await _repository.DeleteManyAsync(stale, autoSave: true);
        }

        return inserts.Count + updates.Count + stale.Count;
    }

    public virtual async Task<List<N11ShipmentCompanyDto>> GetListAsync()
    {
        // Host-global okuma → host'a sabitle (tenant ayrı DB'ye geçse bile merkezî host verisi okunur).
        using (CurrentTenant.Change(null))
        {
            var items = await AsyncExecuter.ToListAsync((await _repository.GetQueryableAsync()).OrderBy(x => x.Name));
            return items.Select(x => ObjectMapper.Map<N11ShipmentCompany, N11ShipmentCompanyDto>(x)).ToList();
        }
    }
}
