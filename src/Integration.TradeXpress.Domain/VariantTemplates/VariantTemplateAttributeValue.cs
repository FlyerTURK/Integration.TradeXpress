namespace Integration.TradeXpress.VariantTemplates;

/// <summary>Şablon özellik değeri (owned → JSON; <see cref="VariantTemplateAttribute.Values"/>). Ör. "Kırmızı", "XL".
/// CASE-KORUR (perakende "XL"/"M" bozulmasın — trim SetAttributes'ta).</summary>
public class VariantTemplateAttributeValue
{
    public string Value { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    public VariantTemplateAttributeValue()
    {
    }

    public VariantTemplateAttributeValue(string value, int displayOrder)
    {
        Value = value;
        DisplayOrder = displayOrder;
    }
}
