namespace Integration.TradeXpress.TrendyolCategories;

/// <summary>Yaprak kategori (tek-lookup seçimi için) — dış id + TAM YOL adı ("Ayakkabı &gt; Kadın &gt; Topuklu").
/// Yaprak adları tekrar ettiğinden ayırt etmek için yol gösterilir (N11 ile simetrik).</summary>
public class TrendyolLeafCategoryDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

/// <summary>Kategori ağacı düğümü (browse) — host-global taksonomiden okunur.</summary>
public class TrendyolCategoryTreeNodeDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string? ParentExternalId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Dip seviye (subCategories boş) mi — ürün yalnız buraya tanımlanır; seçilince attribute'lar on-demand gelir (T2).</summary>
    public bool IsLeaf { get; set; }
}
