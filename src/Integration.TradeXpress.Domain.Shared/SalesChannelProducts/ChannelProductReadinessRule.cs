namespace Integration.TradeXpress.SalesChannelProducts;

/// <summary>
/// Kanal ürününün hazırlık kademesini TEK yerde karar verir (2026-08-19'da
/// <c>SalesChannelProductAppService.ResolveReadiness</c>'ten buraya çıkarıldı). Liste kolonu, varsayılan sıralama
/// ve ürün satışa hazırlık panelinin kanal satırı aynı kuralı okur — kural iki yerde yaşasaydı biri değişince diğeri sessizce
/// eskir, aynı kanal ürünü iki ekranda farklı kademede görünürdü.
/// </summary>
public static class ChannelProductReadinessRule
{
    /// <summary>Reçete yok → <see cref="ChannelProductReadiness.NoRecipe"/>; reçete var ama bugün satılabilir
    /// varyant yok → <see cref="ChannelProductReadiness.NotReady"/>; aksi hâlde <see cref="ChannelProductReadiness.Ready"/>.</summary>
    public static ChannelProductReadiness Resolve(bool hasRecipe, int readyVariantCount)
    {
        if (!hasRecipe)
        {
            return ChannelProductReadiness.NoRecipe;
        }

        return readyVariantCount == 0 ? ChannelProductReadiness.NotReady : ChannelProductReadiness.Ready;
    }
}
