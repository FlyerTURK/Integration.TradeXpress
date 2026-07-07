using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.N11Cities;

/// <summary>
/// N11 adres (İl/İlçe/Mahalle) istemcisi — <b>SOAP CityService</b> (REST /cdn şehri bilmez, keşif 2026-07-06).
/// İl+ilçe host kimliğiyle sync'lenir; mahalleler on-demand (host kimliği — adres verisi seller-özel değil, global).
/// </summary>
public interface IN11CityClient
{
    Task<IReadOnlyList<N11CityRecord>> GetCitiesAsync(string appKey, string appSecret, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<N11DistrictRecord>> GetDistrictsAsync(string cityCode, string appKey, string appSecret, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<N11NeighborhoodRecord>> GetNeighborhoodsAsync(string districtId, string appKey, string appSecret, CancellationToken cancellationToken = default);
}

public sealed record N11CityRecord(string CityCode, string CityId, string CityName);
public sealed record N11DistrictRecord(string DistrictId, string Name);
public sealed record N11NeighborhoodRecord(string Id, string Name);
