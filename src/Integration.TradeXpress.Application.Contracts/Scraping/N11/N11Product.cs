namespace Integration.TradeXpress.Scraping.N11;

/// <summary>n11 kategori ürünü (TEST/scraping demo). Görsel istemcide (Blazor kullanıcı tarayıcısı) yüklenir.
/// Alt kısım = sunucuda hesaplanan kâr analizi (canlı HAS spot + maliyet modeli).</summary>
public class N11Product
{
    // ── Kazınan ham veri ──
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Image { get; set; }
    public string? ListPrice { get; set; }
    public string? CartPrice { get; set; }
    public int? ReviewCount { get; set; }
    public string? ProdId { get; set; }

    // ── Hesaplanan (kâr motoru) ──
    public double? WeightG { get; set; }       // başlıktan ayrıştırılan toplam gram (adet × birim)
    public int? Milyem { get; set; }           // 995 (24 ayar) / 916 (22 ayar)
    public decimal? FairValue { get; set; }    // işçilik dahil has karşılığı (TL)
    public decimal? TotalCost { get; set; }    // has + zarf + kargo + sigorta + n11 komisyon
    public decimal? ProfitTl { get; set; }     // sepet fiyatı − toplam maliyet
    public double? ProfitPct { get; set; }     // kâr / maliyet (oran)
    public double? DiscountPct { get; set; }   // (liste − sepet) / liste (şişirme/sahte indirim)
}
