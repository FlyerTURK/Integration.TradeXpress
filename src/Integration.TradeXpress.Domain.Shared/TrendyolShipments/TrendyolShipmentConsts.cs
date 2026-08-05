namespace Integration.TradeXpress.TrendyolShipments;

/// <summary>Trendyol kargo referansının alan sınırları.</summary>
public static class TrendyolShipmentConsts
{
    /// <summary>Trendyol kargo firması id'si (ör. "7" = Aras). Numerik ama matematik değil → string
    /// (N11Category.ExternalId ile aynı gerekçe).</summary>
    public const int ExternalIdMaxLength = 16;

    /// <summary>Trendyol kısa kodu (ör. "ARASMP").</summary>
    public const int CodeMaxLength = 32;

    public const int NameMaxLength = 128;

    /// <summary>Vergi numarası — TR'de 10 hane; ileride fatura/cari eşleşmesinde kullanılabilsin diye saklanır.</summary>
    public const int TaxNumberMaxLength = 16;
}
