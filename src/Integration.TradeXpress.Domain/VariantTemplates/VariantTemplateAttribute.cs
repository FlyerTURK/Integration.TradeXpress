namespace Integration.TradeXpress.VariantTemplates;

/// <summary>Şablon özellik grubu (owned → JSON; <see cref="VariantTemplate.Attributes"/>). Ör. "Renk" + değerleri.
/// Agnostik <c>EntityAttribute</c>'un şablon karşılığı (sahip entity'ye değil şablona bağlı).</summary>
public class VariantTemplateAttribute
{
    public string Name { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public List<VariantTemplateAttributeValue> Values { get; set; } = new();

    public VariantTemplateAttribute()
    {
    }

    public VariantTemplateAttribute(string name, int displayOrder, List<VariantTemplateAttributeValue> values)
    {
        Name = name;
        DisplayOrder = displayOrder;
        Values = values;
    }
}
