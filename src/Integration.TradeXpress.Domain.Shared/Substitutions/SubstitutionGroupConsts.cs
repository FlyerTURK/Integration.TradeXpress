namespace Integration.TradeXpress.Substitutions;

/// <summary>Muadil grubu alan sınırları (Metal/Scrap katalog konvansiyonuyla hizalı).</summary>
public static class SubstitutionGroupConsts
{
    public const int CodeMaxLength        = 32;
    public const int NameMaxLength        = 128;
    public const int DescriptionMaxLength = 512;

    // Tolerans değeri — gram ya da binde; Metal.StableQuantity ile aynı hassasiyet (N5).
    public const int ToleranceValuePrecision = 18;
    public const int ToleranceValueScale     = 5;
}
