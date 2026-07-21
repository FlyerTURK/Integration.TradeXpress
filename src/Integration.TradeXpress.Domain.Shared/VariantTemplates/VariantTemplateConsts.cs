namespace Integration.TradeXpress.VariantTemplates;

/// <summary>VariantTemplate (varyant tanım katalogu) alan uzunluk sabitleri. Grup adı/değer uzunlukları agnostik
/// nitelik sistemiyle (EntityVariantConsts) hizalı — şablon oradan uygulanacağı için aynı sınırlar geçerli.</summary>
public static class VariantTemplateConsts
{
    public const int CodeMaxLength = 32;
    public const int NameMaxLength = 128;
    public const int DescriptionMaxLength = 512;
}
