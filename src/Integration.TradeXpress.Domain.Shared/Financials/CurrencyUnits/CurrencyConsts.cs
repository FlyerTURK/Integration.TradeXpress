namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>CurrencyUnit / ExchangeRate alanları için merkezî sınırlar.</summary>
public static class CurrencyConsts
{
    /// <summary>Birim kodlari 2 harfe iner ("AD"/Adet — 2026-08-06 Hakan istegi): genel katalog alt siniri 3
    /// (EntityFieldConsts) ama sayim birimlerinin yerlesik kisa kodlari var; sinir YALNIZ CurrencyUnit icin gevser.</summary>
    public const int CodeMinLength        = 2;

    public const int CodeMaxLength        = 16;
    public const int NameMaxLength        = 128;
    public const int DescriptionMaxLength = 512;
    public const int RateSourceMaxLength  = 64;
}
