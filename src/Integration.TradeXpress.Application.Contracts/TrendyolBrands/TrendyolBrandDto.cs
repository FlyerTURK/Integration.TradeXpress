namespace Integration.TradeXpress.TrendyolBrands;

/// <summary>Trendyol markası (type-ahead sonucu) — <see cref="BrandId"/> ürün push'unda ZORUNLU (onaylıda değiştirilemez),
/// <see cref="Name"/> yalnız görüntü/eşleştirme içindir. Marka verisi UÇUCU'dur (milyonlarca marka → tam sync YOK,
/// entity/DB yok); arama endpoint'i SSOT'tur.</summary>
public class TrendyolBrandDto
{
    public long BrandId { get; set; }
    public string Name { get; set; } = string.Empty;
}
