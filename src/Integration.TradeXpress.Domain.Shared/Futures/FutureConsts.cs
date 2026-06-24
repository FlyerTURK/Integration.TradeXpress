namespace Integration.TradeXpress.Futures;

/// <summary>Future (Vadeli) alan sınırları (Cash/VoucherConsts ile hizalı).</summary>
public static class FutureConsts
{
    public const int CodeMaxLength        = 16;
    public const int NameMaxLength        = 128;
    public const int DescriptionMaxLength = 512;

    // FollowingFactor — milyem/lot/saflık çarpanı (N5).
    public const int FactorPrecision = 18;
    public const int FactorScale     = 5;
}
