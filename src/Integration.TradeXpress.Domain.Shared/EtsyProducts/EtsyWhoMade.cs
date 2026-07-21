namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy listeleme <c>who_made</c> alanı — ürünü KİMİN yaptığı (Etsy zorunlu menşe alanı). Ürün-özü karar
/// (<see cref="Integration.TradeXpress.Products.Product"/>'ta yaşar); push'ta Etsy wire string'ine eşlenir
/// (<c>i_did</c>/<c>someone_else</c>/<c>collective</c> — eşleme Application/push katmanında).
/// </summary>
public enum EtsyWhoMade
{
    /// <summary>Satıcının kendisi yaptı (<c>i_did</c>). Varsayılan.</summary>
    IDid = 0,

    /// <summary>Başka biri/üretim ortağı yaptı (<c>someone_else</c>).</summary>
    SomeoneElse = 1,

    /// <summary>Bir kolektif/ekip yaptı (<c>collective</c>).</summary>
    Collective = 2,
}
