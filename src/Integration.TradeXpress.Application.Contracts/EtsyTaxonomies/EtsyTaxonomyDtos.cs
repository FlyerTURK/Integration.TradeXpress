using System.Collections.Generic;

namespace Integration.TradeXpress.EtsyTaxonomies;

/// <summary>Kategori ağacı düğümü (browse) — host-global taksonomiden okunur.</summary>
public class EtsyTaxonomyTreeNodeDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Dip seviye (last-level) mi — ürün yalnız buraya tanımlanır.</summary>
    public bool IsLeaf { get; set; }
}

/// <summary>Yaprak kategori (tek-lookup seçimi için) — dış id + TAM YOL adı ("Accessories &gt; Hats &gt; Beanies").
/// Yaprak adları tekrar edebildiğinden ayırt etmek için tam yol gösterilir.</summary>
public class EtsyLeafCategoryDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string FullPathName { get; set; } = string.Empty;
}

/// <summary>Bir taksonomi düğümünün property (attribute) tanımı — ON-DEMAND (API'den çekilir, KALICI TABLO YOK; yalnız
/// cache). UI zorunlu/varyant alanları buradan sıralar/filtreler. <see cref="PossibleValues"/> boş = serbest/jenerik.</summary>
public class EtsyTaxonomyPropertyDto
{
    public long PropertyId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Kullanıcıya gösterilecek ad (API <c>display_name</c>; yoksa <see cref="Name"/>'e düşer).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Ürün için zorunlu mu.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Varyant ekseni olabilir mi (SKU başına değer).</summary>
    public bool SupportsVariations { get; set; }

    /// <summary>Birden çok değer seçilebilir mi.</summary>
    public bool IsMultivalued { get; set; }

    /// <summary>İzinli maksimum değer sayısı (yoksa null).</summary>
    public int? MaxValuesAllowed { get; set; }

    public List<EtsyTaxonomyPropertyValueDto> PossibleValues { get; set; } = new();
}

/// <summary>Property için önceden tanımlı değer (id-bazlı).</summary>
public class EtsyTaxonomyPropertyValueDto
{
    public long ValueId { get; set; }
    public string Name { get; set; } = string.Empty;
}
