namespace Integration.TradeXpress;

/// <summary>
/// Proje-geneli ortak alan politikası sınırları. Per-entity <c>*MaxLength</c> kendi
/// <c>*Consts</c>'unda kalır; MIN uzunluklar ve DisplayOrder aralığı burada merkezîdir
/// (her entity aynı kuralı paylaşsın).
/// </summary>
public static class EntityFieldConsts
{
    public const int CodeMinLength = 3;

    // NOT: Evrensel min, en kısa meşru ada uymalı — para birimi "Euro" (4) / ileride "Yen"/"Won" (3).
    // Org adları (Company/Branch) için daha yüksek min istenirse entity-özel NameMin'e geçilir.
    public const int NameMinLength = 3;
    public const int DescriptionMinLength = 10;
    public const int DisplayOrderMin = 0;
    public const int DisplayOrderMax = 99;
}
