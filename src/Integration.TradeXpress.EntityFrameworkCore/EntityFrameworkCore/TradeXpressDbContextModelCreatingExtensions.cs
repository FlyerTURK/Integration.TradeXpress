namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Entity mapping'leri OnModelCreating'i şişirmeden, alan-domain bazında extension
/// metotlarında toplar (ABP konvansiyonu). DbContext yalnız <c>builder.ConfigureX()</c> çağırır.
/// Alan bazında partial dosyalara bölünmüştür: Financials / Organization / Commodities / Vouchers.
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    private const int RatePrecision = 18;
    private const int RateScale = 5;
}
