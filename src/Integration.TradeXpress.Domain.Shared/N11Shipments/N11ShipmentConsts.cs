namespace Integration.TradeXpress.N11Shipments;

/// <summary>N11 kargo firması (host-global referans) alan sınırları.</summary>
public static class N11ShipmentConsts
{
    /// <summary>N11 kargo firması id'si (numerik ama matematik yapılmaz → string).</summary>
    public const int ExternalIdMaxLength = 16;

    public const int NameMaxLength = 128;
    public const int ShortNameMaxLength = 32;
}
