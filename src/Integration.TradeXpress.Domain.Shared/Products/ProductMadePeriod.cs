namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürünün NE ZAMAN yapıldığı / dönem kovası — pazaryeri-genel ürün-özü alan (Etsy zorunlu <c>when_made</c>
/// alanının kaynağı ama ONDAN BAĞIMSIZ; push/import'ta Etsy wire string'ine eşlenir — eşleme Application
/// katmanında TEK adapter tablosunda, ör. <c>made_to_order</c>/<c>2020_2026</c>). Üye adları YIL-SABİT
/// (Etsy'nin rolling-yıl kovası <c>2020_2026</c> her yıl kayar; yalnız wire-map satırı değişir, enum + DB
/// değeri sabit kalır). Numaralama KRONOLOJİK (yeni→eski); Etsy openapi <c>when_made</c> enum'unun 19 değeriyle
/// birebir (K9 kararı 2026-07-23; eski 8-kovalı <c>EtsyWhenMade</c>'in genişletilmiş halefi — canlı veri %100
/// <c>MadeToOrder=0</c> olduğundan yeniden-numaralama veri-remap gerektirmedi, dry-run kanıtı
/// <c>.claude/research/marketplace/K9-madeperiod-remap-dryrun.md</c>). Enum üyeleri rakamla başlayamadığından
/// yıl kovaları <c>Y</c> ön-ekiyle adlandırılır.
/// </summary>
public enum ProductMadePeriod
{
    /// <summary>Sipariş üzerine üretilir (<c>made_to_order</c>). Varsayılan.</summary>
    MadeToOrder = 0,

    /// <summary>2020 ve sonrası (rolling kova; bugünkü wire <c>2020_2026</c> — üst sınır her yıl kayar).</summary>
    Y2020Plus = 1,

    /// <summary>2010–2019 (<c>2010_2019</c>).</summary>
    Y2010To2019 = 2,

    /// <summary>2007–2009 (<c>2007_2009</c>).</summary>
    Y2007To2009 = 3,

    /// <summary>2007 öncesi — jenerik süper-küme kovası (<c>before_2007</c>); dekat bilinmiyorsa kullanılır.</summary>
    Before2007 = 4,

    /// <summary>2000–2006 (<c>2000_2006</c>).</summary>
    Y2000To2006 = 5,

    /// <summary>1990'lar (<c>1990s</c>) — vintage.</summary>
    Y1990s = 6,

    /// <summary>1980'ler (<c>1980s</c>) — vintage.</summary>
    Y1980s = 7,

    /// <summary>1970'ler (<c>1970s</c>) — vintage.</summary>
    Y1970s = 8,

    /// <summary>1960'lar (<c>1960s</c>) — vintage.</summary>
    Y1960s = 9,

    /// <summary>1950'ler (<c>1950s</c>) — vintage.</summary>
    Y1950s = 10,

    /// <summary>1940'lar (<c>1940s</c>) — vintage.</summary>
    Y1940s = 11,

    /// <summary>1930'lar (<c>1930s</c>) — vintage.</summary>
    Y1930s = 12,

    /// <summary>1920'ler (<c>1920s</c>) — vintage.</summary>
    Y1920s = 13,

    /// <summary>1910'lar (<c>1910s</c>) — vintage.</summary>
    Y1910s = 14,

    /// <summary>1900'ler (<c>1900s</c>) — antika.</summary>
    Y1900s = 15,

    /// <summary>1800'ler (<c>1800s</c>) — antika.</summary>
    Y1800s = 16,

    /// <summary>1700'ler (<c>1700s</c>) — antika.</summary>
    Y1700s = 17,

    /// <summary>1700 öncesi (<c>before_1700</c>) — antika.</summary>
    Before1700 = 18,
}
