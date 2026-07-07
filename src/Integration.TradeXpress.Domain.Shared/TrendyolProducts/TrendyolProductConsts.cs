namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>Trendyol ürün listeleme alan sınırları (Domain.Shared — entity + EF + DTO paylaşır).</summary>
public static class TrendyolProductConsts
{
    /// <summary>Trendyol kategori id'si (numerik ama string tutulur — matematiksel değil).</summary>
    public const int CategoryIdMaxLength = 32;

    public const int CategoryNameMaxLength = 512;

    /// <summary>Trendyol marka id'si (numerik ama string tutulur).</summary>
    public const int BrandIdMaxLength = 32;

    /// <summary>Batch istek kimliği (async submit yanıtı).</summary>
    public const int BatchRequestIdMaxLength = 128;

    /// <summary>Batch/işlem durumu (PROCESSING/COMPLETED/FAILED ...).</summary>
    public const int StatusMaxLength = 64;

    public const int LastErrorMaxLength = 4000;

    /// <summary>Serbest (custom) attribute değeri — id'siz metin.</summary>
    public const int CustomAttributeValueMaxLength = 2000;
}
