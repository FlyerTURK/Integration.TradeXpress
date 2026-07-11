using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Trendyol;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Trendyol SİPARİŞ istemcisi — Trendyol Marketplace API v2 (REST/JSON, apigw.trendyol.com). SALT-OKUMA (GET):
/// satıcının siparişlerini tarih-penceresi + sayfa döngüsüyle çeker. Trendyol'a SIFIR yazma. Model ÇÖZÜLMÜŞ döner.
/// <para><b>Endpoint CANLI DOĞRULANDI (2026-07-11):</b> <c>GET /integration/order/sellers/{sellerId}/orders?startDate=&amp;endDate=&amp;page=&amp;size=</c>.
/// <c>startDate/endDate</c> ZORUNLU değil ama verilmezse Trendyol yalnız son ~2 haftayı döndürür → tüm geçmiş için
/// 14 günlük pencereler dolaşılır (aralık ≤ 2 hafta şartı). 429'a dayanıklılık ortak tabandadır.</para>
/// </summary>
public interface ITrendyolOrderClient
{
    /// <summary>Belirli bir 14 günlük pencere içindeki siparişlerin BİR SAYFASINI çeker (salt GET). Tarihler epoch-ms.</summary>
    Task<TrendyolOrdersPage> GetOrdersPageAsync(TrendyolCredentials credentials, long startDateEpochMs, long endDateEpochMs, int page, int size, CancellationToken cancellationToken = default);

    /// <summary><paramref name="sinceUtc"/>'den ŞİMDİ'ye kadar TÜM siparişleri 14 günlük pencereler + her pencerede
    /// sayfa döngüsüyle çeker (salt GET). Trendyol'un "yalnız son ~2 hafta" varsayılanını aşar.</summary>
    Task<IReadOnlyList<RemoteOrder>> GetAllOrdersAsync(TrendyolCredentials credentials, DateTime sinceUtc, int pageSize = 200, CancellationToken cancellationToken = default);
}

/// <summary>Trendyol sipariş listeleme sayfası — sayfalama zarfı + kanal-agnostik <see cref="RemoteOrder"/>'lar.</summary>
public sealed record TrendyolOrdersPage(
    int Page,
    int Size,
    int TotalPages,
    long TotalElements,
    IReadOnlyList<RemoteOrder> Items);
