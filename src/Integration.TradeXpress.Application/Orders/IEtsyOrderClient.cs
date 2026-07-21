using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Etsy SİPARİŞ (receipt) istemcisi — Etsy Open API v3 (REST/JSON, api.etsy.com/v3). SALT-OKUMA (GET): mağazanın
/// receipt'lerini limit/offset sayfa döngüsüyle çeker (<c>getShopReceipts</c>, scope <c>transactions_r</c>). Etsy'ye
/// SIFIR yazma. Model kanal-agnostik <see cref="RemoteOrder"/> döner (receipt → sipariş, transaction → kalem; Etsy
/// para tutarları money-object <c>{amount,divisor}</c> → decimal). Auth: access token <see cref="EtsyCredentials.ChannelId"/>
/// üzerinden token sağlayıcıdan (refresh şeffaf) + <c>x-api-key</c> başlığı.
/// </summary>
public interface IEtsyOrderClient
{
    /// <summary>Mağazanın TÜM receipt'lerini (limit/offset sayfa döngüsü) çeker (salt GET). Etsy tüm geçmişi tutar →
    /// N11 seed deseniyle hizalı (tarih filtresi yok; sayfalama tümünü kapsar).</summary>
    Task<IReadOnlyList<RemoteOrder>> GetAllOrdersAsync(EtsyCredentials credentials, int pageSize = 100, CancellationToken cancellationToken = default);
}

/// <summary>Etsy sipariş istemcisi kimlik demeti — token sağlayıcı için <see cref="ChannelId"/>, <c>x-api-key</c> için
/// birleşik <see cref="ApiKeyHeader"/> (<c>{keystring}:{secret}</c>), receipts ucu için <see cref="ShopId"/>.</summary>
public sealed record EtsyCredentials(Guid ChannelId, string ApiKeyHeader, string ShopId);
