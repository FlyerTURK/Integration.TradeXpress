using System.Collections.Generic;

namespace Integration.TradeXpress.N11Categories;

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
