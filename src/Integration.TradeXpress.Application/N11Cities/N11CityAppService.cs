using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.N11;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.N11Cities;

/// <summary>
/// N11 adres taksonomisi AppService — host-global İl/İlçe sync/okuma + on-demand Mahalle. Sync HOST kimliğiyle
/// (config <c>N11:CategorySync:*</c> — host N11 hesabı, kategori+şehir ortak). Mahalleler saklanmaz; il/ilçe
/// seçilince SOAP'tan çekilir. Kaynak SOAP CityService (REST'te yok).
///
/// <para><b>Yetki (2026-08-07 G1):</b> sınıf POLİCY'SİZ <c>[Authorize]</c> — okuma uçlarını adres seçicileri
/// KANAL-DIŞI ekranlardan da tüketiyor (GeographyAppService → tenant/şirket adres formları), kanal iznine
/// bağlamak o formları kırardı; kimlik yeter. Yazan sync ucu ayrıca <c>SalesChannels.Update</c> ister
/// (N11Category emsali). Öncesinde sınıf tamamen ANONİMDİ ve içerideki "host-only" ters guard'ı anonim istekte
/// tenant null olduğundan GEÇİYORDU — stale-silmeli tam re-sync dışarıdan tetiklenebilirdi.</para>
/// </summary>
[Authorize]
public class N11CityAppService : TradeXpressAppService, IN11CityAppService
{
    private readonly IRepository<N11City, Guid> _cityRepository;
    private readonly IRepository<N11District, Guid> _districtRepository;
    private readonly IN11CityClient _client;
    private readonly IN11HostCredentialResolver _credentialResolver;
    private readonly IDistributedCache<N11NeighborhoodCacheItem> _neighborhoodCache;
    private readonly N11CitySyncManager _syncManager;

    public N11CityAppService(
        IRepository<N11City, Guid> cityRepository,
        IRepository<N11District, Guid> districtRepository,
        IN11CityClient client,
        IN11HostCredentialResolver credentialResolver,
        IDistributedCache<N11NeighborhoodCacheItem> neighborhoodCache,
        N11CitySyncManager syncManager)
    {
        _cityRepository = cityRepository;
        _districtRepository = districtRepository;
        _client = client;
        _credentialResolver = credentialResolver;
        _neighborhoodCache = neighborhoodCache;
        _syncManager = syncManager;
    }

    /// <summary>Sync çekirdeği <see cref="N11CitySyncManager"/>'da — worker aynı çekirdeği İZİNSİZ tüketir
    /// (kullanıcı kimliği yok), [Authorize] yalnız bu HTTP ucundadır.</summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual Task<int> SyncCitiesAndDistrictsAsync()
    {
        return _syncManager.SyncCitiesAndDistrictsAsync();
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
        // On-demand: adres verisi seller-özel değil → host kimliğiyle çekilir; DB'ye SAKLANMAZ (bilinçli istisna
        // korunuyor — GeographyAppService.GetNeighborhoodsAsync). Ama artık 6 saat DAĞITIK CACHE'lenir.
        // GEREKÇE (2026-07-25 meclis kararı): boş adres formu artık ilk ilçeyi OTOMATİK seçtiğinden (Hakan kararı)
        // bu SOAP çağrısı HER form açılışında tetikleniyor; N11CityClient timeout'u 30 sn (N11CityClient.cs:23),
        // yani her açılış Blazor Server circuit'ini 30 sn'ye kadar bloklayabilirdi. Mahalle REFERANS verisidir
        // (idari kararla değişir, fiyat/stok gibi saniyelik değil) → 6 saatlik bayatlık kabul edilebilir ve TTL
        // kendini tazeler (kalıcı tabloda olmayan özellik: orada bayat satırı temizleyecek sahip yok).
        // Desen: TrendyolCategoryAppService.GetLeafAttributesCachedAsync (aynı problem sınıfı, kanıtlı).
        var normalized = (districtId ?? string.Empty).Trim();

        var cached = await _neighborhoodCache.GetOrAddAsync(
            $"N11Neighborhoods:{normalized}",
            async () =>
            {
                // Hata cache'lenmez: istisna GetOrAddAsync'ten YÜKSELİR → sonraki çağrı yeniden dener.
                var (appKey, appSecret) = await _credentialResolver.ResolveAsync();
                var list = await _client.GetNeighborhoodsAsync(normalized, appKey, appSecret);
                return new N11NeighborhoodCacheItem
                {
                    Items = list.Select(n => new N11NeighborhoodDto { Id = n.Id, Name = n.Name }).ToList(),
                };
            },
            () => new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6),
            });

        return cached?.Items ?? new List<N11NeighborhoodDto>();
    }
}
