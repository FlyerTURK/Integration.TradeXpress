namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil motorunun TEKNİK neden kodları — TEK KAYNAK (SSOT). <c>SubstitutionSolver</c> üretir,
/// DTO'lar ham taşır (lokalize DEĞİL), UI okunur metne çevirir. Domain.Shared'da yaşar ki
/// Blazor.Client (Domain'i referans alamaz) ile Domain aynı sabitleri paylaşsın.
/// </summary>
public static class SubstitutionReasonCodes
{
    /// <summary>Ön-filtre nedeni: tek parça, talep + tolerans üst sınırını aşıyor (hiçbir kombinasyona giremez).</summary>
    public const string PieceWeightExceedsTarget = "PieceWeightExceedsTarget";

    /// <summary>Ön-filtre nedeni: kullanılabilir adet ≤ 0.</summary>
    public const string NoStock = "NoStock";

    /// <summary>Başarısızlık nedeni: tüm emtiaların TÜM stoğu kullanıldı, yine de talebe ulaşılamadı.</summary>
    public const string StockExhausted = "StockExhausted";

    /// <summary>Başarısızlık nedeni ÖN EKİ: kalan fark ("Remainder:0.6" — sayı invariant kültürde).</summary>
    public const string RemainderPrefix = "Remainder:";
}
