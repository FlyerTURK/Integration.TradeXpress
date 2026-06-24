namespace Integration.TradeXpress.Scraps;

/// <summary>Scrap (Hurda) alan sınırları (Future/VoucherConsts ile hizalı).</summary>
public static class ScrapConsts
{
    public const int CodeMaxLength        = 16;
    public const int NameMaxLength        = 128;
    public const int DescriptionMaxLength = 512;

    // Purity (milyem/saflık) — 0..1 arası, N5.
    public const int PurityPrecision = 18;
    public const int PurityScale     = 5;

    public const decimal DefaultPurity = 0.570m;
}
