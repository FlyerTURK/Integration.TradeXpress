namespace Integration.TradeXpress.Substitutions;

/// <summary>Muadil grubu alan sınırları (Metal/Scrap katalog konvansiyonuyla hizalı).</summary>
public static class SubstitutionGroupConsts
{
    public const int CodeMaxLength        = 32;
    public const int NameMaxLength        = 128;
    public const int DescriptionMaxLength = 512;

    /// <summary>Miktar birimi KISALTMASI ("gr", "kg", "lt", "adet") — gösterim metinlerinde kullanılır.
    /// Kısa tutulur: kombinasyon adında her parçadan sonra tekrarlanıyor.</summary>
    public const int QuantityUnitMaxLength = 8;

    /// <summary>Birim belirtilmediğinde kullanılan varsayılan — bugünkü tüm muadil grupları kuyum/maden
    /// tarafında ve gram bazlı; mevcut kayıtlar bu değerle geriye dönük tutarlı kalır.</summary>
    public const string DefaultQuantityUnit = "gr";

    // Tolerans değeri — gram ya da binde; Metal.StableQuantity ile aynı hassasiyet (N5).
    public const int ToleranceValuePrecision = 18;
    public const int ToleranceValueScale     = 5;
}
