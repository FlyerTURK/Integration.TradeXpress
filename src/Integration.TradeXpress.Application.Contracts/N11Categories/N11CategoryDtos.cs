using System.Collections.Generic;

namespace Integration.TradeXpress.N11Categories;

/// <summary>Yaprak kategori (tek-lookup seçimi için) — dış id + TAM YOL adı ("Elektronik &gt; Telefon &gt; Akıllı Telefon").
/// Yaprak adları çok tekrar ettiğinden (Diğer/Aksesuar...) ayırt etmek için yol gösterilir.</summary>
public class N11LeafCategoryDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

/// <summary>Kategori ağacı düğümü (browse) — host-global taksonomiden okunur.</summary>
public class N11CategoryTreeNodeDto
{
    public string ExternalId { get; set; } = string.Empty;
    public string? ParentExternalId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Dip seviye (last-level) mi — ürün yalnız buraya tanımlanır; seçilince attribute'lar on-demand gelir.</summary>
    public bool IsLeaf { get; set; }
}

/// <summary>Yaprak kategori attribute'u (on-demand; SAKLANMAZ). SalesChannel'ın kendi kimliğiyle çekilir.</summary>
public class N11CategoryAttributeDto
{
    public string AttributeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
    public bool IsVariant { get; set; }
    public bool IsCustomValue { get; set; }

    /// <summary>N11'in form öncelik sırası (WSDL: xs:double). UI sıralaması bunu kullanır; çözülemezse null → sona.</summary>
    public double? Priority { get; set; }

    public List<N11CategoryAttributeValueDto> Values { get; set; } = new();
}

/// <summary>Attribute value — <see cref="ValueId"/> REST'te dolu (listelemede kullanılır), SOAP fallback'te null.</summary>
public class N11CategoryAttributeValueDto
{
    public string? ValueId { get; set; }
    public string Value { get; set; } = string.Empty;
}

/// <summary>Komisyon TSV import raporu — eşleşme sayıları + eşleşmeyen/muğlak/geçersiz satırlar (görev kuralı:
/// sessiz geçilmez, kullanıcıya gösterilir).</summary>
public class N11CommissionImportResultDto
{
    /// <summary>TSV'deki geçerli satır sayısı.</summary>
    public int TotalRowCount { get; set; }

    /// <summary>Bir yaprağa eşlenen satır sayısı (tekrar eden yaprak eşleşmeleri tek sayılır).</summary>
    public int MatchedCount { get; set; }

    /// <summary>SetCommission uygulanan kategori sayısı.</summary>
    public int UpdatedCategoryCount { get; set; }

    /// <summary>DB'deki toplam yaprak sayısı (kapsam görünürlüğü).</summary>
    public int LeafCount { get; set; }

    /// <summary>Eşleşmeyen/muğlak TSV satırları (yol + neden).</summary>
    public List<string> UnmatchedRows { get; set; } = new();

    /// <summary>Aynı yaprağa çakışan oranla düşen satırlar.</summary>
    public List<string> ConflictRows { get; set; } = new();

    /// <summary>Parse edilemeyen satırlar (satır no + neden).</summary>
    public List<string> InvalidRows { get; set; } = new();
}
