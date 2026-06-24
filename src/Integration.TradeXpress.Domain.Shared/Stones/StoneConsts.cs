namespace Integration.TradeXpress.Stones;

/// <summary>Stone (Taş) alan sınırları.</summary>
public static class StoneConsts
{
    public const int CodeMaxLength        = 16;
    public const int NameMaxLength        = 128;
    public const int DescriptionMaxLength = 512;
    public const int AttributeMaxLength   = 64;   // Cins/Tür/Renk/Kesim/Saflık/Elek/Kategori/Grup

    public const int PricePrecision = 18;
    public const int PriceScale     = 5;
}
