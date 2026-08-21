using System.Collections.Generic;

namespace Integration.TradeXpress.N11Cities;

/// <summary>
/// N11 mahalle listesinin dağıtık-cache taşıyıcısı. <c>IDistributedCache&lt;T&gt;</c> bir SINIF ister
/// (<c>List&lt;T&gt;</c> doğrudan verilemez) — bu tip yalnız o taşıma işini yapar, başka sorumluluğu yoktur.
/// </summary>
public class N11NeighborhoodCacheItem
{
    public List<N11NeighborhoodDto> Items { get; set; } = new();
}
