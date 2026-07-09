using System;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.SalesChannels.Etsy;

/// <summary>
/// Etsy API çağrıları için GEÇERLİ access token sağlayıcı — süresi dolmuşsa refresh grant'ıyla tazeler ve
/// ROTASYONLU yeni refresh token'ı DB'ye geri yazar (Etsy her yenilemede yeni refresh döner; eskisi kaybolursa
/// bağlantı kopar → persist atomik ve yarış-korumalı). Sonraki dilimlerin (ürün push vb.) tek giriş kapısı:
/// istemci kodu token ömrü/rotasyon bilmez, yalnız bunu çağırır.
/// </summary>
public interface IEtsyTokenProvider
{
    /// <summary>Kanalın geçerli access token'ını döner (gerekirse yeniler). Kanal bağlı değilse ya da refresh token
    /// süresi geçtiyse (90 gün pasif → yeniden bağlan) <c>...:Etsy:NotConnected</c> fırlatır.</summary>
    Task<string> GetAccessTokenAsync(Guid channelId, CancellationToken cancellationToken = default);
}
