namespace Integration.TradeXpress.SpecialCodes;

/// <summary>Özel kod (SpecialCode — herhangi bir entity property'sini gruplayan hiyerarşik kod sözlüğü) alan
/// sınırları.</summary>
public static class SpecialCodeConsts
{
    public const int CodeMaxLength         = 32;
    public const int NameMaxLength         = 128;
    public const int EntityNameMaxLength   = 128;   // teknik: hedef entity tipi adı (ör. "Good")
    public const int PropertyNameMaxLength = 128;   // teknik: hedef property adı (ör. "Category")
    public const int DescriptionMaxLength  = 512;
}
