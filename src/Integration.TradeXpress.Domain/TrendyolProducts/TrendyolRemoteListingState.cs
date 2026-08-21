using System;
using System.Collections.Generic;
using Integration.TradeXpress.SalesChannelProducts;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Pazaryerinin BİR kalem hakkında bildirdiği her şey — içe aktarımın kanal-kaydına yazdığı tek paket.
///
/// <para><b>Neden tek kayıt:</b> bu değerlerin hepsi aynı yanıttan, aynı anda, aynı kalem için gelir ve hepsi
/// aynı semantiği taşır ("pazaryeri şu an ne diyor"). Ayrı ayrı parametre olarak taşımak
/// <c>UpsertImportedSku</c>'yu on dört argümanlı hâle getirir ve sıra hatasını sessiz kılardı.</para>
///
/// <para><b>Her alan üç durumludur:</b> <c>null</c> = "bildirilmedi/okunmadı", dolu değer = beyan. Bu ayrım
/// yazma davranışını belirler: <c>null</c> alan MEVCUDU KORUR, ezmez — kimlik-only çağrılar (yeniden-bağlama)
/// daha önce okunmuş gerçek değerleri sessizce silmesin diye.</para>
/// </summary>
public sealed record TrendyolRemoteListingState(
    int? Quantity = null,
    decimal? ListPrice = null,
    decimal? SalePrice = null,
    bool? Archived = null,
    bool? Locked = null,
    string? LockReason = null,
    bool? Blacklisted = null,
    string? BlacklistReason = null,
    bool? Rejected = null,
    string? RejectReason = null,
    bool? HasActiveCampaign = null,
    string? ProductUrl = null,
    DateTime? CreatedAtUtc = null,
    DateTime? UpdatedAtUtc = null,
    IReadOnlyList<SalesChannelTrTrendyolProductSkuRemoteAxisValue>? AxisValues = null);

/// <summary>
/// Pazaryeri engel bayraklarını TEK cevaba indirir — "bu kalem neden kanalda satılamıyor?".
///
/// <para><b>Neden türetilir, saklanmaz:</b> bayrakların kendisi zaten kayıtta duruyor. İkinci bir alan olarak
/// saklamak, bayraklarla çelişebilen bir kopya üretirdi (SSOT ihlali). Türetme ucuz ve her okuyanda aynı.</para>
///
/// <para><b>Sıralama keyfî değil AĞIRLIĞA göre:</b> bir kalem hem karalistede hem kilitli olabilir (canlı
/// örnekte dördü öyle). Kullanıcıya iki gerekçe birden yazmak eylemi bulanıklaştırır; önce ÇÖZÜLMESİ GEREKEN
/// söylenir. Karaliste belge/itiraz süreci ister, kilit tedarik sorunudur — biri çözülmeden diğeri anlamsızdır.</para>
///
/// <para><b>ENGEL PUSH'U DURDURMAZ — yalnız GÖRÜNÜR KILAR</b> (bilinçli karar, CLAUDE.md "mekanizma ≠ politika"
/// ile aynı çizgi). İki gerekçe: ① bu bayraklar SON İÇE AKTARIM anının snapshot'ıdır; karaliste kalkmış olabilir
/// ve bayat bir bayrağa dayanıp gönderimi kesmek, çözülmüş bir sorunu kalıcı hâle getirirdi. ② Reddi karşı taraf
/// zaten veriyor ve artık PushHistory'ye kendi cümlesiyle yazılıyor — bizim ayrıca kesmemiz, kullanıcının
/// göremediği ikinci bir engel olurdu. Sistem uyarır, kararı kullanıcı verir.</para>
/// </summary>
public static class TrendyolListingObstacleResolver
{
    /// <summary>
    /// AĞIRLIK SIRASININ TEK YERİ — bayraklardan engel: Blacklisted &gt; Rejected &gt; Locked &gt; Archived.
    /// SKU aşırı yüklemesi de, kayıt-seviyesi SQL projeksiyonunun toplanmış bayrakları da BURADAN geçer;
    /// sıra bir daha ikinci bir yerde yazılmaz (bağımsız denetim bulgusu: App katmanı aynı if-zincirini
    /// yeniden kurmuştu — biri değişse diğeri sessizce sapardı).
    /// </summary>
    public static ChannelListingObstacle Resolve(bool blacklisted, bool rejected, bool locked, bool archived)
    {
        if (blacklisted)
        {
            return ChannelListingObstacle.Blacklisted;
        }

        if (rejected)
        {
            return ChannelListingObstacle.Rejected;
        }

        if (locked)
        {
            return ChannelListingObstacle.Locked;
        }

        if (archived)
        {
            return ChannelListingObstacle.Archived;
        }

        return ChannelListingObstacle.None;
    }

    /// <summary>Engelin GEREKÇESİ — engele karşılık gelen bayrağın cümlesi; engel yoksa/arşivse <c>null</c>
    /// (arşivin gerekçesi olmaz). Kayıt-seviyesi projeksiyon da bunu kullanır — gerekçe eşlemesi tek yerde.</summary>
    public static string? ResolveReason(
        ChannelListingObstacle obstacle, string? blacklistReason, string? rejectReason, string? lockReason)
    {
        switch (obstacle)
        {
            case ChannelListingObstacle.Blacklisted:
                return blacklistReason;
            case ChannelListingObstacle.Rejected:
                return rejectReason;
            case ChannelListingObstacle.Locked:
                return lockReason;
            default:
                return null;
        }
    }

    /// <summary>Kalemin engelini çözer. Hiçbir bayrak bildirilmemişse <see cref="ChannelListingObstacle.None"/>.</summary>
    public static ChannelListingObstacle Resolve(SalesChannelTrTrendyolProductSku sku)
    {
        return Resolve(
            sku.RemoteBlacklisted == true,
            sku.RemoteRejected == true,
            sku.RemoteLocked == true,
            sku.RemoteArchived == true);
    }

    /// <summary>Kaydın engeli — SKU'ları arasındaki EN AĞIR engel. Tek kalemi engelli bir kayıt "engelsiz"
    /// sayılamaz: o kalem satılamıyorsa kullanıcının haberi olmalıdır.</summary>
    public static ChannelListingObstacle Resolve(System.Collections.Generic.IEnumerable<SalesChannelTrTrendyolProductSku> skus)
    {
        var worst = ChannelListingObstacle.None;
        foreach (var sku in skus)
        {
            var obstacle = Resolve(sku);
            if (obstacle == ChannelListingObstacle.None)
            {
                continue;
            }

            // Enum sırası ağırlık sırasıdır (Blacklisted=1 en ağır); None=0 olduğu için ayrıca elendi.
            if (worst == ChannelListingObstacle.None || obstacle < worst)
            {
                worst = obstacle;
            }
        }

        return worst;
    }

    /// <summary>Engelin PAZARYERİ GEREKÇESİ — kanalın kendi cümlesi, yeniden yazılmaz. Engel yoksa <c>null</c>.
    /// Gerekçe bildirilmemişse de <c>null</c> döner: engelin VARLIĞI ile GEREKÇESİ ayrı sorulardır.</summary>
    public static string? ResolveReason(SalesChannelTrTrendyolProductSku sku)
    {
        return ResolveReason(Resolve(sku), sku.RemoteBlacklistReason, sku.RemoteRejectReason, sku.RemoteLockReason);
    }
}
