using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// N11 SİPARİŞ istemcisi — SOAP <c>OrderService</c>. Okuma (<c>DetailedOrderList</c>/<c>getOrderDetail</c>) +
/// Sipariş Fazı O2'den itibaren YAZMA (<c>OrderItemAccept</c>/<c>OrderItemReject</c>/<c>MakeOrderItemShipment</c> —
/// GERÇEK pazaryerine, geri alınamaz). Kanal-agnostik <see cref="RemoteOrder"/> üretir (Trendyol istemcisiyle AYNI
/// tip → <see cref="OrderAppService"/> upsert'i tek gövde).
/// <para><b>Canlı doğrulandı (2026-07-11):</b> endpoint + auth + istek biçimi çalışıyor. <b>Tarih filtresi
/// GÖNDERİLMEZ:</b> N11 sipariş geçmişini UZUN saklar (test hesabında 2017'ye kadar 106 sipariş) — <c>period</c>
/// verilince yalnız o aralık gelir ve eski siparişler gizlenir; verilmeyince N11 TÜM geçmişi döndürür (istediğimiz).
/// N11 bu ucu SIKI throttle'lar (&quot;belli süre aralıklarıyla güncellenebilir&quot;) → istemci bekleyip tekrar dener.
/// Sipariş modeli KALEM-MERKEZLİ (order → orderItemList); istemci order düzeyine düzleştirir.</para>
/// <para><b>YAZMA uçları THROTTLE-RETRY YAPMAZ</b> (okumanın tersine) — belirsiz hatada otomatik tekrar çift-
/// aksiyon riski taşır (ör. aynı kalem iki kez kabul edilmeye çalışılır); tek deneme, hata dostane fırlatılır.</para>
/// </summary>
public interface IN11OrderClient
{
    /// <summary>BİR SAYFA sipariş çeker (salt-okuma; tarih filtresi YOK → tüm geçmiş). Streaming çek-kaydet için:
    /// çağıran sayfayı alıp order başına kaydeder, sonra sonraki sayfayı ister. Throttle'da bekleyip tekrar dener.</summary>
    Task<N11OrdersPage> GetOrdersPageAsync(string appKey, string appSecret, int page, CancellationToken cancellationToken = default);

    /// <summary>Bir siparişin ZENGİN detayını çeker (SOAP <c>getOrderDetail</c>, N11 sipariş id ile; salt-okuma) →
    /// kanal-agnostik <see cref="OrderDetailSnapshot"/> (alıcı/adresler/tutar kırılımı/kalem komisyon+nitelik). Sync
    /// order-başına çağırır + DB'ye saklar (popup DB'den okur; canlı çağrı yok). Detay çekilemezse (id boş / SOAP
    /// hatası) <c>null</c> — çağıran siparişi yine kaydeder (enrichment; snapshot felsefesi).</summary>
    Task<OrderDetailSnapshot?> GetOrderDetailAsync(string appKey, string appSecret, string n11OrderId, DateTime fetchedAt, CancellationToken cancellationToken = default);

    /// <summary>Bir veya BİRDEN ÇOK sipariş kalemini N11'e TEK istekte KABUL olarak bildirir (SOAP <c>OrderItemAccept</c>
    /// — WSDL'de <c>orderItemList</c> zaten liste + TEK <c>numberOfPackages</c>; siparişin tüm kalemleri aynı pakette
    /// gönderiliyorsa bu, N11'in kendi doğal biçimidir) — GERÇEK, geri alınamaz. Başarısızlıkta (N11 ResultInfo hata)
    /// dostane <c>BusinessException</c> fırlatır.</summary>
    Task AcceptOrderItemAsync(string appKey, string appSecret, IReadOnlyList<long> n11OrderItemIds, int numberOfPackages, CancellationToken cancellationToken = default);

    /// <summary>Bir veya BİRDEN ÇOK sipariş kalemini N11'e TEK istekte RED olarak bildirir (SOAP <c>OrderItemReject</c>)
    /// — GERÇEK, geri alınamaz.</summary>
    Task RejectOrderItemAsync(string appKey, string appSecret, IReadOnlyList<long> n11OrderItemIds, string reason, CancellationToken cancellationToken = default);

    /// <summary>Sipariş kaleminin kargo bilgisini N11'e bildirir (SOAP <c>MakeOrderItemShipment</c>) — GERÇEK, geri
    /// alınamaz. <paramref name="shipmentCompanyId"/> N11'in kendi kargo firması id'si (ör. 344=Yurtiçi Kargo).</summary>
    Task MakeShipmentAsync(
        string appKey, string appSecret, long n11OrderItemId, string shipmentCompanyId,
        string trackingNumber, string? campaignNumber, int shipmentMethod, CancellationToken cancellationToken = default);
}

/// <summary>N11 sipariş listeleme sayfası — kanal-agnostik siparişler + toplam sayfa sayısı (döngü koşulu).</summary>
public sealed record N11OrdersPage(IReadOnlyList<RemoteOrder> Orders, int PageCount);
