using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.N11;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.N11Cities;

/// <summary>
/// N11 il/ilçe SYNC çekirdeği — <b>izinsiz iç servis</b> (2026-08-07 G1 ayrıştırması).
///
/// <para><b>Neden app service'ten çıkarıldı:</b> sync'i hem kullanıcı akışı (app service ucu — artık
/// <c>[Authorize(SalesChannels.Update)]</c> kapılı) hem <c>N11ReferenceSyncWorker</c> (arka plan, kullanıcı
/// kimliği YOK) tüketiyor. Worker <c>[Authorize]</c>'lı uçtan geçemez — CLAUDE.md §6 materyalizer deseni:
/// worker bağlamı yetkili yolu KULLANMAZ, çekirdek izinsiz servise iner, kapı yalnız HTTP yüzeyinde durur.</para>
/// </summary>
public class N11CitySyncManager : ITransientDependency
{
    private readonly IRepository<N11City, Guid> _cityRepository;
    private readonly IRepository<N11District, Guid> _districtRepository;
    private readonly IN11CityClient _client;
    private readonly IN11HostCredentialResolver _credentialResolver;
    private readonly ICurrentTenant _currentTenant;

    public N11CitySyncManager(
        IRepository<N11City, Guid> cityRepository,
        IRepository<N11District, Guid> districtRepository,
        IN11CityClient client,
        IN11HostCredentialResolver credentialResolver,
        ICurrentTenant currentTenant)
    {
        _cityRepository = cityRepository;
        _districtRepository = districtRepository;
        _client = client;
        _credentialResolver = credentialResolver;
        _currentTenant = currentTenant;
    }

    public virtual async Task<int> SyncCitiesAndDistrictsAsync()
    {
        // Host-only guard KORUNUR: [Authorize] kimliği doğrular, bu satır BAĞLAMI ("host verisine yalnız host
        // bağlamı yazar"). Worker host bağlamında koştuğundan geçer; tenant kullanıcı app service ucundan
        // gelirse dostane hatayla döner.
        if (_currentTenant.Id is not null)
        {
            throw new BusinessException("TradeXpress:N11:CitySyncHostOnly");
        }

        var (appKey, appSecret) = await _credentialResolver.ResolveAsync();
        var count = 0;

        // İller
        var cities = await _client.GetCitiesAsync(appKey, appSecret);
        var existingCities = (await _cityRepository.GetListAsync()).ToDictionary(x => x.CityCode, StringComparer.Ordinal);
        var cityInserts = new List<N11City>();
        var cityUpdates = new List<N11City>();
        foreach (var c in cities)
        {
            if (existingCities.TryGetValue(c.CityCode, out var entity))
            {
                if (!string.Equals(entity.Name, c.CityName, StringComparison.Ordinal))
                {
                    entity.SetName(c.CityName);
                    cityUpdates.Add(entity);
                }
            }
            else
            {
                cityInserts.Add(new N11City(c.CityCode, c.CityId, c.CityName));
            }
        }

        if (cityInserts.Count > 0)
        {
            await _cityRepository.InsertManyAsync(cityInserts, autoSave: true);
        }

        if (cityUpdates.Count > 0)
        {
            await _cityRepository.UpdateManyAsync(cityUpdates, autoSave: true);
        }

        count += cityInserts.Count + cityUpdates.Count;

        // İlçeler (il başına GetDistrict). Not: GetDistrict hata verirse client throw eder → metod erken çıkar,
        // stale-silme adımına HİÇ ulaşılmaz (kısmi/yanlış silme olmaz — güvenli).
        var existingDistricts = (await _districtRepository.GetListAsync()).ToDictionary(x => x.DistrictId, StringComparer.Ordinal);
        var districtInserts = new Dictionary<string, N11District>(StringComparer.Ordinal);
        var districtUpdates = new List<N11District>();
        var fetchedDistrictIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in cities)
        {
            var districts = await _client.GetDistrictsAsync(c.CityCode, appKey, appSecret);
            foreach (var d in districts)
            {
                fetchedDistrictIds.Add(d.DistrictId);
                if (existingDistricts.TryGetValue(d.DistrictId, out var entity))
                {
                    if (!string.Equals(entity.Name, d.Name, StringComparison.Ordinal) ||
                        !string.Equals(entity.CityCode, c.CityCode, StringComparison.Ordinal))
                    {
                        entity.SetName(d.Name);
                        entity.SetCityCode(c.CityCode);   // ilçe il değiştirdiyse (nadir) düzelt
                        districtUpdates.Add(entity);
                    }
                }
                else if (!districtInserts.ContainsKey(d.DistrictId))
                {
                    districtInserts[d.DistrictId] = new N11District(d.DistrictId, c.CityCode, d.Name);
                }
            }
        }

        if (districtInserts.Count > 0)
        {
            await _districtRepository.InsertManyAsync(districtInserts.Values, autoSave: true);
        }

        if (districtUpdates.Count > 0)
        {
            await _districtRepository.UpdateManyAsync(districtUpdates, autoSave: true);
        }

        // Stale temizliği: N11'de artık olmayan (silinen) ilçeleri DB'den kaldır (tam re-sync).
        var staleDistricts = existingDistricts.Values.Where(x => !fetchedDistrictIds.Contains(x.DistrictId)).ToList();
        if (staleDistricts.Count > 0)
        {
            await _districtRepository.DeleteManyAsync(staleDistricts, autoSave: true);
        }

        count += districtInserts.Count + districtUpdates.Count + staleDistricts.Count;
        return count;
    }
}
