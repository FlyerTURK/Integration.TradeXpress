using System;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannelProducts;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannelProducts;

/// <summary>
/// Kanal-aksiyon bileşeninin (<c>ChannelProductActions</c>) TEK girdisi — "hangi kanal ürünü, hangi düğmeler
/// açık". Bileşen dört farklı ekranda çiziliyor (ürün satışa hazırlık paneli · ürün formu kanal sekmesi · kanal
/// ürünleri listesi) ve her ekranın satır DTO'su başka; düğme uygunluğunu her ekranda ayrı türetmek kuralı dörde
/// kopyalamak olurdu. Bu record o dört DTO'dan tek şekle indirger; <c>ChannelProductActions</c> yalnız bunu okur.
///
/// <para><b>Kaynak önceliği:</b> sunucu <c>Can*</c> veriyorsa (<see cref="ChannelReadinessRowDto"/>) olduğu gibi
/// alınır — kural sunucudadır. Vermiyorsa (graf DTO'ları, birleşik liste satırı) eski panellerin görünür davranışı
/// birebir korunarak türetilir; bu türetmeler "ne zaman anlamlı" kadarıdır, sunucu guard'ının yerine geçmez.</para>
/// </summary>
public sealed record ChannelProductActionContext(
    SalesChannelType ChannelType,
    Guid ChannelProductId,
    Guid SalesChannelId,
    bool CanPush,
    bool CanSyncStockPrice,
    bool CanRefreshStatus,
    bool CanResolveQueue)
{
    /// <summary>Kanal ürünü KAYDEDİLMİŞ mi — kaydedilmemiş (Id boş) satırda hiçbir aksiyon sunucuya gidemez.</summary>
    public bool IsPersisted
    {
        get { return ChannelProductId != Guid.Empty; }
    }

    /// <summary>Satışa hazırlık paneli satırından — sunucu Can* bayraklarını zaten hesaplamış, olduğu gibi alınır.</summary>
    public static ChannelProductActionContext From(ChannelReadinessRowDto row)
    {
        return new ChannelProductActionContext(
            row.ChannelType,
            row.ChannelProductId,
            row.SalesChannelId,
            CanPush: row.CanPush,
            CanSyncStockPrice: row.CanSyncStockPrice,
            CanRefreshStatus: row.CanRefreshStatus,
            CanResolveQueue: row.CanResolveQueue);
    }

    /// <summary>Birleşik kanal-ürün liste satırından. Satır hafiftir (batch/task kimliği taşımaz), o yüzden
    /// uygunluk nötr senkron durumundan TÜRETİLİR:
    /// <list type="bullet">
    /// <item>Gönder: kaydedilmiş her satırda (sunucu guard'ı — doğrulanmış varyant yoksa — push'un kendisinde çalışır).</item>
    /// <item>Stok-Fiyat: uzak kimliği OLAN satırda (N11 ürün id · Trendyol ana kod · Etsy listing) — kanalda karşılığı
    /// olmayan ürünün stoğu güncellenemez.</item>
    /// <item>Durumu Yenile: Trendyol'da hiç gönderilmemiş DIŞINDAKİ her durumda (batch id listede yok; "yok"u sunucu söyler).</item>
    /// <item>Kuyruk sorgusu: N11'de yalnız <c>Pending</c> (bekleyen task bu durumun tanımıdır).</item>
    /// </list></summary>
    public static ChannelProductActionContext From(SalesChannelProductListDto row)
    {
        var hasRemote = !string.IsNullOrWhiteSpace(row.RemoteId);
        return new ChannelProductActionContext(
            row.ChannelType,
            row.Id,
            row.SalesChannelId,
            CanPush: row.ChannelType != SalesChannelType.Etsy,
            CanSyncStockPrice: row.ChannelType != SalesChannelType.Etsy && hasRemote,
            CanRefreshStatus: row.ChannelType == SalesChannelType.TrTrendyol && row.SyncState != ChannelProductSyncState.NotSent,
            CanResolveQueue: row.ChannelType == SalesChannelType.TrN11 && row.SyncState == ChannelProductSyncState.Pending);
    }

    /// <summary>Ürün grafındaki N11 kanal ürününden — ürün formu kanal sekmesinin eski görünür davranışı:
    /// Gönder hep, Stok-Fiyat yalnız N11 ürün kimliği varsa; kuyruk sorgusu bekleyen task varsa.</summary>
    public static ChannelProductActionContext From(SalesChannelTrN11ProductDto dto)
    {
        return new ChannelProductActionContext(
            SalesChannelType.TrN11,
            dto.Id,
            dto.SalesChannelId,
            CanPush: true,
            CanSyncStockPrice: dto.N11ProductId.HasValue,
            CanRefreshStatus: false,
            CanResolveQueue: !string.IsNullOrWhiteSpace(dto.PendingPushTaskId));
    }

    /// <summary>Ürün grafındaki Trendyol kanal ürününden — Gönder hep; Durumu Yenile ve Stok-Fiyat yalnız bir batch
    /// açılmışsa (ürün kanala hiç gitmediyse yenilenecek durum da, güncellenecek stok da yoktur).</summary>
    public static ChannelProductActionContext From(SalesChannelTrTrendyolProductDto dto)
    {
        var hasBatch = !string.IsNullOrWhiteSpace(dto.BatchRequestId);
        return new ChannelProductActionContext(
            SalesChannelType.TrTrendyol,
            dto.Id,
            dto.SalesChannelId,
            CanPush: true,
            CanSyncStockPrice: hasBatch,
            CanRefreshStatus: hasBatch,
            CanResolveQueue: false);
    }

    /// <summary>Ürün grafındaki Etsy kanal ürününden — Etsy push/senkron bu sürümde YOK; bileşen bunu metinle söyler.</summary>
    public static ChannelProductActionContext From(SalesChannelEtsyProductDto dto)
    {
        return new ChannelProductActionContext(
            SalesChannelType.Etsy,
            dto.Id,
            dto.SalesChannelId,
            CanPush: false,
            CanSyncStockPrice: false,
            CanRefreshStatus: false,
            CanResolveQueue: false);
    }
}
