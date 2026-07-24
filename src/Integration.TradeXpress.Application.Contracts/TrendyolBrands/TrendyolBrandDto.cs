namespace Integration.TradeXpress.TrendyolBrands;

/// <summary>Trendyol markası (canlı type-ahead sonucu YA DA write-through cache satırı) — <see cref="BrandId"/> ürün
/// push'unda ZORUNLU (onaylıda değiştirilemez), <see cref="Name"/> yalnız görüntü/eşleştirme içindir. Marka evreni
/// ~780K kayıt → TAM sync YOK; canlı arama endpoint'i SSOT, yalnız SEÇİLEN markalar cache'lenir (K3 hybrid).</summary>
public class TrendyolBrandDto
{
    public long BrandId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Trendyol "luxury" bayrağı (API'de hazır) — cache'e write-through ile taşınır.</summary>
    public bool IsLuxury { get; set; }
}
