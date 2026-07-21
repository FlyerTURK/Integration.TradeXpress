namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy listeleme <c>when_made</c> alanı — ürünün NE ZAMAN yapıldığı/dönem kovası (Etsy zorunlu menşe alanı).
/// Ürün-özü karar (<see cref="Integration.TradeXpress.Products.Product"/>'ta yaşar); push'ta Etsy wire string'ine
/// eşlenir (ör. <c>made_to_order</c>/<c>2020_2025</c> — eşleme Application/push katmanında). Enum üyeleri rakamla
/// başlayamadığından yıl kovaları <c>Y</c> ön-ekiyle adlandırılır. Makul kova seti (Etsy dönem seçenekleri).
/// </summary>
public enum EtsyWhenMade
{
    /// <summary>Sipariş üzerine üretilir (<c>made_to_order</c>). Varsayılan.</summary>
    MadeToOrder = 0,

    /// <summary>2020–2025 (<c>2020_2025</c>).</summary>
    Y2020_2025 = 1,

    /// <summary>2010–2019 (<c>2010_2019</c>).</summary>
    Y2010_2019 = 2,

    /// <summary>2006–2009 (<c>2006_2009</c>).</summary>
    Y2006_2009 = 3,

    /// <summary>2000–2005 (<c>2000_2005</c>).</summary>
    Y2000_2005 = 4,

    /// <summary>1990'lar (<c>1990s</c>) — vintage.</summary>
    Y1990s = 5,

    /// <summary>1980'ler (<c>1980s</c>) — vintage.</summary>
    Y1980s = 6,

    /// <summary>2000 öncesi / daha eski (<c>before_2000</c>) — vintage/antika kovası.</summary>
    Before2000 = 7,
}
