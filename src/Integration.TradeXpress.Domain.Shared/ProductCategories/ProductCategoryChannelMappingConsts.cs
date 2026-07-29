namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Çekirdek kategori ↔ satış kanalı kategorisi eşleştirmesinin alan sınırları. Kanal kategorisi kimliği
/// METİNDİR: N11/Trendyol/Etsy taksonomileri kendi dış kimliklerini string olarak verir (<c>N11Category.ExternalId</c>
/// deseni) ve bizim Guid'imizle ilgisi yoktur.
/// </summary>
public static class ProductCategoryChannelMappingConsts
{
    /// <summary>Kanal kategori dış kimliği — <c>N11CategoryConsts.ExternalIdMaxLength</c> ile hizalı.</summary>
    public const int ChannelCategoryIdMaxLength = 64;

    /// <summary>Kanal kategori adı SNAPSHOT'ı (yalnız gösterim; kanal taksonomisi değişirse bayatlayabilir —
    /// doğruluk kimlikte, okunabilirlik burada). Kanal ad alanlarının en genişiyle hizalı.</summary>
    public const int ChannelCategoryNameMaxLength = 512;

    /// <summary>Kanal NİTELİK dış kimliği — üç pazaryerinin de nitelik kimliği bu sınıra sığar
    /// (N11 sayısal, Trendyol sayısal, Etsy property id). Metin tutulur: tipleri farklı.</summary>
    public const int ChannelAttributeIdMaxLength = 64;

    /// <summary>Kanal nitelik adı SNAPSHOT'ı (yalnız gösterim; kategoriyle aynı gerekçe).</summary>
    public const int ChannelAttributeNameMaxLength = 256;
}
