using System.Collections.Generic;

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

/// <summary>Yaprak kategori attribute'u (on-demand; kalıcı SAKLANMAZ, 6 saat cache'li). Id-bazlı — N11'in name/value'suyla
/// simetrik ama değerler <see cref="TrendyolAttributeValueDto.ValueId"/> ile bağlanır.</summary>
public class TrendyolLeafAttributeDto
{
    public int AttributeId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Zorunlu attribute — push öncesi dolu olmalı (fail-fast T6); UI'da işaretlenir.</summary>
    public bool Required { get; set; }

    /// <summary>Varyant ekseni (renk vb.) — ürün seviyesinde değil, SKU/varyant başına seçilir; ürün formunda gizlenir.</summary>
    public bool Varianter { get; set; }

    /// <summary>Serbest (custom) metin izinli — <c>AttributeValueId</c> yerine <c>CustomValue</c> yazılır.</summary>
    public bool AllowCustom { get; set; }

    public List<TrendyolAttributeValueDto> Values { get; set; } = new();
}

/// <summary>Attribute value — id-bazlı; seçilince <c>AttributeValueId</c> olarak yazılır.</summary>
public class TrendyolAttributeValueDto
{
    public int ValueId { get; set; }
    public string Value { get; set; } = string.Empty;
}
