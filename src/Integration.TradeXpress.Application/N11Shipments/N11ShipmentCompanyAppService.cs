using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// N11 kargo firması AppService — host-global sync/okuma. Sync HOST kimliğiyle (config <c>N11:CategorySync:*</c>,
/// tüm N11 host-sync'leri ortak) + tam re-sync (ekle/güncelle/SİL). Okuma <c>CurrentTenant.Change(null)</c> ile
/// host'a sabitlenir (db-per-tenant'a karşı merkezilik garantisi). Kaynak SOAP.
///
/// <para><b>Yetki (2026-08-07 G1):</b> tüketiciler yalnız KANAL ekranları (sihirbaz + provisioner + şablon
/// drill'i) → kanal ailesiyle aynı sınır (<c>SalesChannels.Default</c>, N11Category emsali); stale-silmeli
/// sync ayrıca <c>Update</c> ister. Öncesinde sınıf ANONİMDİ — silme içeren tam re-sync dışarıdan
/// tetiklenebilirdi. Sync çekirdeği <see cref="N11ShipmentCompanySyncManager"/>'da (worker izinsiz tüketir).</para>
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class N11ShipmentCompanyAppService : TradeXpressAppService, IN11ShipmentCompanyAppService
{
    private readonly IRepository<N11ShipmentCompany, Guid> _repository;
    private readonly N11ShipmentCompanySyncManager _syncManager;

    public N11ShipmentCompanyAppService(
        IRepository<N11ShipmentCompany, Guid> repository,
        N11ShipmentCompanySyncManager syncManager)
    {
        _repository = repository;
        _syncManager = syncManager;
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual Task<int> SyncAsync()
    {
        return _syncManager.SyncAsync();
    }

    /// <summary>Şablonlarda SEÇİLEBİLİR kargo firmaları — kısa kodu OLMAYANLAR elenir.
    /// <para>Gerekçe (2026-07-26 canlı API testi): N11 firmayı <c>shortName</c>'den çözer; kısa kodu boş olan
    /// firmalar (Asil/DHL/Fillo Kargo) şablona EKLENEMEZ — boş kod da uydurma kod da
    /// <i>"shipmentCompanyShortName alanı boş olamaz"</i> ile reddedilir. Listede bırakmak kullanıcıyı
    /// anlaşılmaz bir push hatasına sürükler; yansıma verisi ise N11 gerçeği olarak DOKUNULMADAN durur.</para></summary>
    public virtual async Task<List<N11ShipmentCompanyDto>> GetListAsync()
    {
        // Host-global okuma → host'a sabitle (tenant ayrı DB'ye geçse bile merkezî host verisi okunur).
        using (CurrentTenant.Change(null))
        {
            var items = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.ShortName != null && x.ShortName != "")
                    .OrderBy(x => x.Name));
            return items.Select(x => ObjectMapper.Map<N11ShipmentCompany, N11ShipmentCompanyDto>(x)).ToList();
        }
    }
}
