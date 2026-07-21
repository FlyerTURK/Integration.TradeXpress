namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy listeleme türü (<c>type</c>) — fiziksel gönderilen ürün mü yoksa dijital indirilebilir mi. Kargo profili +
/// işleme süresi yalnız fiziksel listelemede anlamlıdır. Varsayılan <see cref="Physical"/>.
/// </summary>
public enum EtsyListingType
{
    /// <summary>Fiziksel ürün — kargolanır (<c>physical</c>). Varsayılan.</summary>
    Physical = 0,

    /// <summary>Dijital indirilebilir ürün (<c>download</c>).</summary>
    Download = 1,
}
