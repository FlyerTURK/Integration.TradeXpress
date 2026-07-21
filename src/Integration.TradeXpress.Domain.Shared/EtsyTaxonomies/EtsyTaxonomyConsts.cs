namespace Integration.TradeXpress.EtsyTaxonomies;

/// <summary>Etsy seller taxonomy (host-global referans taksonomi) alan sınırları.</summary>
public static class EtsyTaxonomyConsts
{
    /// <summary>Etsy taxonomy node id'si (numerik ama matematik yapılmaz → string). String genişçe tutulur.</summary>
    public const int ExternalIdMaxLength = 32;

    public const int NameMaxLength = 256;
}
