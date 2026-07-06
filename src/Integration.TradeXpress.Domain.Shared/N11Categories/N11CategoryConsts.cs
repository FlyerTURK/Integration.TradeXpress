namespace Integration.TradeXpress.N11Categories;

/// <summary>N11 kategori (host-global referans taksonomi) alan sınırları.</summary>
public static class N11CategoryConsts
{
    /// <summary>N11 kategori id'si (numerik ama matematik yapılmaz → string). En büyük id 7 hane; string genişçe tutulur.</summary>
    public const int ExternalIdMaxLength = 32;

    public const int NameMaxLength = 256;
}
