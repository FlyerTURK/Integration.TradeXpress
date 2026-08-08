using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// N11 SİPARİŞ SOAP istemcisinin TEST sahtesi — ağ yok.
///
/// <para><b>Çağrıları KAYDEDER</b> (<see cref="ListCalls"/>): seed kolunun tarih filtresiz, delta kolunun dar
/// pencereyle çağırdığını doğrulamanın tek yolu budur. Yalnız dönen siparişlere bakan bir sahte, iki kolun
/// karışmasını göremezdi — ikisi de aynı listeyi döndürürdü.</para>
///
/// <para>Sayfalama sadeleştirilmiştir: tüm siparişler İLK sayfada döner (gerçek client'ın sayfa döngüsü kendi
/// birim testlerinde sürülüyor; burada sınanan senkron ZİNCİRİ).</para>
/// </summary>
public sealed class FakeN11OrderClient : IN11OrderClient
{
    /// <summary>Sahte pazaryeri envanteri — testler buraya sipariş koyar.</summary>
    public List<RemoteOrder> RemoteOrders { get; } = new();

    /// <summary>Sipariş id → detay snapshot (opsiyonel; yoksa detay <c>null</c> döner = enrichment atlanır).</summary>
    public Dictionary<string, OrderDetailSnapshot> Details { get; } = new(StringComparer.Ordinal);

    /// <summary>Liste çağrılarının kaydı: hangi sayfa, hangi pencereyle istendi.</summary>
    public List<(int Page, DateTime? SinceUtc)> ListCalls { get; } = new();

    /// <summary>Detay çağrılarının kaydı — açık sipariş tazelemesinin gerçekten koştuğunun kanıtı.</summary>
    public List<string> DetailCalls { get; } = new();

    public Task<N11OrdersPage> GetOrdersPageAsync(
        string appKey, string appSecret, int page, DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        ListCalls.Add((page, sinceUtc));

        var orders = page == 0 ? RemoteOrders.ToList() : new List<RemoteOrder>();
        return Task.FromResult(new N11OrdersPage(orders, PageCount: RemoteOrders.Count == 0 ? 0 : 1));
    }

    public Task<OrderDetailSnapshot?> GetOrderDetailAsync(
        string appKey, string appSecret, string n11OrderId, DateTime fetchedAt, CancellationToken cancellationToken = default)
    {
        DetailCalls.Add(n11OrderId);
        return Task.FromResult(Details.TryGetValue(n11OrderId, out var detail) ? detail : null);
    }

    // ── YAZMA uçları: testte gerçek pazaryerine hiçbir şey gitmez; çağrılırsa kaydedilir. ──

    public List<long> AcceptedItemIds { get; } = new();
    public List<long> RejectedItemIds { get; } = new();
    public List<long> ShippedItemIds { get; } = new();

    public Task AcceptOrderItemAsync(
        string appKey, string appSecret, IReadOnlyList<long> n11OrderItemIds, int numberOfPackages, CancellationToken cancellationToken = default)
    {
        AcceptedItemIds.AddRange(n11OrderItemIds);
        return Task.CompletedTask;
    }

    public Task RejectOrderItemAsync(
        string appKey, string appSecret, IReadOnlyList<long> n11OrderItemIds, string reason, CancellationToken cancellationToken = default)
    {
        RejectedItemIds.AddRange(n11OrderItemIds);
        return Task.CompletedTask;
    }

    public Task MakeShipmentAsync(
        string appKey, string appSecret, long n11OrderItemId, string shipmentCompanyId,
        string trackingNumber, string? campaignNumber, int shipmentMethod, CancellationToken cancellationToken = default)
    {
        ShippedItemIds.Add(n11OrderItemId);
        return Task.CompletedTask;
    }

    /// <summary>Testler arası sızıntıyı önler (sahte SINGLETON kayıtlı — koleksiyon paylaşılır).</summary>
    public void Reset()
    {
        RemoteOrders.Clear();
        Details.Clear();
        ListCalls.Clear();
        DetailCalls.Clear();
        AcceptedItemIds.Clear();
        RejectedItemIds.Clear();
        ShippedItemIds.Clear();
    }
}
