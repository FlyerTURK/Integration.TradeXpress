namespace Integration.TradeXpress.Countries;

/// <summary>Country alan sınırları.</summary>
public static class CountryConsts
{
    /// <summary>ISO-3166 alpha-2 (TR, US, DE...).</summary>
    public const int CodeMaxLength = 2;
    public const int NameMaxLength = 128;

    /// <summary>ISO 3166-1 alpha-3 (TUR, USA...) — sabit 3 harf.</summary>
    public const int Alpha3CodeLength = 3;

    /// <summary>ISO 3166-1 numeric (792, 840...) — sabit 3 hane string (matematik yapılmaz).</summary>
    public const int NumericCodeLength = 3;
}
