using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.N11;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.N11Cities;

/// <summary>
/// N11 adres taksonomisi AppService — host-global İl/İlçe sync/okuma + on-demand Mahalle. Sync HOST kimliğiyle
/// (config <c>N11:CategorySync:*</c> — host N11 hesabı, kategori+şehir ortak). Mahalleler saklanmaz; il/ilçe
/// seçilince SOAP'tan çekilir. Kaynak SOAP CityService (REST'te yok).
/// </summary>
public class N11CityAppService : TradeXpressAppService, IN11CityAppService
{
    private readonly IRepository<N11City, Guid> _cityRepository;
    private readonly IRepository<N11District, Guid> _districtRepository;
    private readonly IN11CityClient _client;
    private readonly IN11HostCredentialResolver _credentialResolver;

    public N11CityAppService(
        IRepository<N11City, Guid> cityRepository,
        IRepository<N11District, Guid> districtRepository,
        IN11CityClient client,
        IN11HostCredentialResolver credentialResolver)
    {
        _cityRepository = cityRepository;
        _districtRepository = districtRepository;
        _client = client;
        _credentialResolver = credentialResolver;
    }

    public virtual async Task<int> SyncCitiesAndDistrictsAsync()
    {
        if (CurrentTenant.Id is not null)
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

    public virtual async Task<List<N11CityDto>> GetCitiesAsync()
    {
        // Host-global okuma → host'a sabitle (db-per-tenant'a karşı merkezilik garantisi).
        using (CurrentTenant.Change(null))
        {
            var items = await AsyncExecuter.ToListAsync((await _cityRepository.GetQueryableAsync()).OrderBy(x => x.Name));
            return items.Select(x => ObjectMapper.Map<N11City, N11CityDto>(x)).ToList();
        }
    }

    public virtual async Task<List<N11DistrictDto>> GetDistrictsAsync(string cityCode)
    {
        var normalized = (cityCode ?? string.Empty).Trim();
        using (CurrentTenant.Change(null))
        {
            var items = await AsyncExecuter.ToListAsync(
                (await _districtRepository.GetQueryableAsync()).Where(x => x.CityCode == normalized).OrderBy(x => x.Name));
            return items.Select(x => ObjectMapper.Map<N11District, N11DistrictDto>(x)).ToList();
        }
    }

    public virtual async Task<List<N11NeighborhoodDto>> GetNeighborhoodsAsync(string districtId)
    {
        // On-demand: adres verisi seller-özel değil → host kimliğiyle çekilir; saklanmaz.
        var (appKey, appSecret) = await _credentialResolver.ResolveAsync();
        var list = await _client.GetNeighborhoodsAsync((districtId ?? string.Empty).Trim(), appKey, appSecret);
        return list.Select(n => new N11NeighborhoodDto { Id = n.Id, Name = n.Name }).ToList();
    }
}
