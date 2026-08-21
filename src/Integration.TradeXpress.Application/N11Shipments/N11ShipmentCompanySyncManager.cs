using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// N11 kargo firması SYNC çekirdeği — <b>izinsiz iç servis</b> (2026-08-07 G1 ayrıştırması; gerekçe
/// <c>N11CitySyncManager</c>'da: worker <c>[Authorize]</c>'lı uçtan geçemez, [Authorize] yalnız HTTP ucunda).
/// </summary>
public class N11ShipmentCompanySyncManager : ITransientDependency
{
    private readonly IRepository<N11ShipmentCompany, Guid> _repository;
    private readonly IN11ShipmentCompanyClient _client;
    private readonly IConfiguration _configuration;
    private readonly ICurrentTenant _currentTenant;

    public N11ShipmentCompanySyncManager(
        IRepository<N11ShipmentCompany, Guid> repository,
        IN11ShipmentCompanyClient client,
        IConfiguration configuration,
        ICurrentTenant currentTenant)
    {
        _repository = repository;
        _client = client;
        _configuration = configuration;
        _currentTenant = currentTenant;
    }

    public virtual async Task<int> SyncAsync()
    {
        // Host-only guard KORUNUR — [Authorize] app service ucunda, bağlam kontrolü burada (N11CitySyncManager notu).
        if (_currentTenant.Id is not null)
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
}
