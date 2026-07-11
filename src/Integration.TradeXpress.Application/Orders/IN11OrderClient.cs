using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// N11 SİPARİŞ istemcisi — SOAP <c>OrderService.DetailedOrderList</c> (salt-okuma). N11'e SIFIR yazma. Kanal-agnostik
/// <see cref="RemoteOrder"/> üretir (Trendyol istemcisiyle AYNI tip → <see cref="OrderAppService"/> upsert'i tek gövde).
/// <para><b>Canlı doğrulandı (2026-07-11):</b> endpoint + auth + istek biçimi çalışıyor. <b>Tarih filtresi
/// GÖNDERİLMEZ:</b> N11 sipariş geçmişini UZUN saklar (test hesabında 2017'ye kadar 106 sipariş) — <c>period</c>
/// verilince yalnız o aralık gelir ve eski siparişler gizlenir; verilmeyince N11 TÜM geçmişi döndürür (istediğimiz).
/// N11 bu ucu SIKI throttle'lar (&quot;belli süre aralıklarıyla güncellenebilir&quot;) → istemci bekleyip tekrar dener.
/// Sipariş modeli KALEM-MERKEZLİ (order → orderItemList); istemci order düzeyine düzleştirir.</para>
/// </summary>
public interface IN11OrderClient
{
    /// <summary>Kanalın TÜM siparişlerini sayfa döngüsüyle çeker (salt-okuma; tarih filtresi YOK → tüm geçmiş).
    /// Throttle'da bekleyip tekrar dener.</summary>
    Task<IReadOnlyList<RemoteOrder>> GetAllOrdersAsync(string appKey, string appSecret, CancellationToken cancellationToken = default);
}
