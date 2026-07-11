using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Trendyol;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Trendyol SİPARİŞ REST istemcisinin TEST sahtesi — testte ağ yok (READ-ONLY ilkenin test aynası). Sahte ağı VE
/// tarih-pencerelerini yok sayar: <see cref="RemoteOrders"/>'a konan siparişleri <see cref="GetAllOrdersAsync"/> TEK
/// sefer döndürür (gerçek client pencere/sayfa döngüsünü yapar; onun saf döngüleri ayrı birim testlerde doğrulanır).
/// </summary>
public sealed class FakeTrendyolOrderClient : ITrendyolOrderClient
{
    public List<RemoteOrder> RemoteOrders { get; } = new();

    public Task<TrendyolOrdersPage> GetOrdersPageAsync(
        TrendyolCredentials credentials, long startDateEpochMs, long endDateEpochMs, int page, int size, CancellationToken cancellationToken = default)
    {
        var items = page == 0 ? RemoteOrders.ToList() : new List<RemoteOrder>();
        var totalPages = RemoteOrders.Count == 0 ? 0 : 1;
        return Task.FromResult(new TrendyolOrdersPage(page, size, totalPages, RemoteOrders.Count, items));
    }

    public Task<IReadOnlyList<RemoteOrder>> GetAllOrdersAsync(
        TrendyolCredentials credentials, DateTime sinceUtc, int pageSize = 200, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<RemoteOrder>>(RemoteOrders.ToList());
    }
}
