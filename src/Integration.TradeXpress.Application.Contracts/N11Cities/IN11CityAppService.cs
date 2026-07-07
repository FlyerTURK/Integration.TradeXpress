using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.N11Cities;

/// <summary>
/// N11 adres taksonomisi — host-global İl/İlçe (sync + okuma) + on-demand Mahalle. İl+ilçe HOST kimliğiyle bir kez
/// sync'lenir (tüm tenant'lar paylaşır); mahalleler il/ilçe seçilince on-demand çekilir (saklanmaz).
/// </summary>
public interface IN11CityAppService : IApplicationService
{
    /// <summary>Host-only: 81 il + ilçelerini SOAP'tan çekip upsert eder. Toplam eklenen+güncellenen sayısını döner.</summary>
    Task<int> SyncCitiesAndDistrictsAsync();

    /// <summary>Tüm iller (host-global, DB'den).</summary>
    Task<List<N11CityDto>> GetCitiesAsync();

    /// <summary>Bir ilin ilçeleri (host-global, DB'den).</summary>
    Task<List<N11DistrictDto>> GetDistrictsAsync(string cityCode);

    /// <summary>On-demand: bir ilçenin mahalleleri (SOAP'tan, host kimliğiyle; saklanmaz).</summary>
    Task<List<N11NeighborhoodDto>> GetNeighborhoodsAsync(string districtId);
}
