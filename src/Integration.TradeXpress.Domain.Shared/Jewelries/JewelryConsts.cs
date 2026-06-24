namespace Integration.TradeXpress.Jewelries;

/// <summary>Jewelry (Mücevher) alan sınırları.</summary>
public static class JewelryConsts
{
    public const int CodeMaxLength        = 16;
    public const int NameMaxLength        = 128;
    public const int DescriptionMaxLength = 512;
    public const int AttributeMaxLength   = 64;   // Model/Cins/Tür/Renk/Kategori/Grup

    public const int PricePrecision = 18;
    public const int PriceScale     = 5;
}
