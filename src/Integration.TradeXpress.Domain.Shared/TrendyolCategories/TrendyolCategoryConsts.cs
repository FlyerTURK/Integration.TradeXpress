namespace Integration.TradeXpress.TrendyolCategories;

/// <summary>Trendyol kategori (host-global referans taksonomi) alan sınırları — <see cref="N11Categories"/> ikizi.</summary>
public static class TrendyolCategoryConsts
{
    /// <summary>Trendyol kategori id'si (numerik ama matematik yapılmaz → string). String genişçe tutulur.</summary>
    public const int ExternalIdMaxLength = 32;

    /// <summary>Kategori adı — Trendyol adları uzun olabilir (ör. "Kadın Kol Saati").</summary>
    public const int NameMaxLength = 512;
}
