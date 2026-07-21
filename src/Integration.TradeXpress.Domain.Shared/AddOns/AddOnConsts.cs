namespace Integration.TradeXpress.AddOns;

/// <summary>AddOn (sipariş eklentisi) alan uzunluk/precision sabitleri. Min uzunluklar + DisplayOrder aralığı
/// merkezi <see cref="EntityFieldConsts"/>'ta. Fiyat decimal(18,5) — emtia fiyat precision'ıyla hizalı.</summary>
public static class AddOnConsts
{
    public const int CodeMaxLength = 32;
    public const int NameMaxLength = 128;
    public const int DescriptionMaxLength = 512;

    public const int PricePrecision = 18;
    public const int PriceScale = 5;
}
