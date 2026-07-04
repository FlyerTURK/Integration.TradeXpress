namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// <see cref="ProcessDirectionType"/> yön-konvansiyonunun TEK kaynağı (DRY): ÇİFT enum değeri
/// (Inbound=0, Credit=2, Buy=4) = giriş (inflow, bakiyeye +), TEK değer (Outbound=1, Debit=3,
/// Sell=5) = çıkış (outflow, bakiyeye −). Sayısal değerler ve çift/tek eşlemesi
/// <c>ProcessDirectionTypeTests</c> ile KİLİTLİDİR — enum yeniden sıralanırsa test kırmızı yanar.
/// NOT: EF Core'a çevrilen IQueryable sorgularında bu extension KULLANILAMAZ (method çağrısı
/// SQL'e translate edilemez) — oralarda ham <c>(int)Direction % 2 == 0</c> deseni bilinçli kalır.
/// </summary>
public static class ProcessDirectionTypeExtensions
{
    /// <summary>Yön "giriş" mi (bakiyeye + yönde): Inbound/Credit/Buy → çift enum değeri.</summary>
    public static bool IsInflow(this ProcessDirectionType direction)
    {
        return ((int)direction % 2) == 0;
    }

    /// <summary>Yön "çıkış" mı (bakiyeye − yönde): Outbound/Debit/Sell → tek enum değeri.</summary>
    public static bool IsOutflow(this ProcessDirectionType direction)
    {
        return !direction.IsInflow();
    }
}
