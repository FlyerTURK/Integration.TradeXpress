namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>Trendyol ürün listeleme alan sınırları (Domain.Shared — entity + EF + DTO paylaşır).</summary>
public static class TrendyolProductConsts
{
    /// <summary>Trendyol kategori id'si (numerik ama string tutulur — matematiksel değil).</summary>
    public const int CategoryIdMaxLength = 32;

    public const int CategoryNameMaxLength = 512;

    /// <summary>Trendyol marka id'si (numerik ama string tutulur).</summary>
    public const int BrandIdMaxLength = 32;

    /// <summary>Marka görüntü adı (arama sonucundan; opsiyonel).</summary>
    public const int BrandNameMaxLength = 256;

    /// <summary>Varyant grup anahtarı (productMainId — "{ÜrünKodu}-{SequenceNo}", frozen). Trendyol V2 sınırı 40;
    /// ürün kodu (32) + sıra eki payı.</summary>
    public const int ProductMainIdMaxLength = 64;

    /// <summary>Trendyol satıcı-geneli barcode (DONDURULMUŞ, "{VaryantKodu}-{SequenceNo}"). Trendyol V2 sınırı 40;
    /// üretilen/GTIN kodlar için pay.</summary>
    public const int BarcodeMaxLength = 64;

    /// <summary>Trendyol stok kodu (= merchantSku; mutable). Trendyol V2 sınırı 100.</summary>
    public const int StockCodeMaxLength = 100;

    /// <summary>Kanal-özel açıklama (HTML; opsiyonel). Boşsa push'ta ürün açıklaması devralınır. Trendyol V2 sınırı 30.000.</summary>
    public const int DescriptionMaxLength = 30000;

    /// <summary>Batch istek kimliği (async submit yanıtı).</summary>
    public const int BatchRequestIdMaxLength = 128;

    /// <summary>Batch işlem tipi (ProductV2OnBoarding/ProductV2Update/ProductInventoryUpdate ...).</summary>
    public const int BatchRequestTypeMaxLength = 64;

    /// <summary>Batch/işlem durumu (PROCESSING/COMPLETED/FAILED ...).</summary>
    public const int StatusMaxLength = 64;

    public const int LastErrorMaxLength = 4000;

    /// <summary>Serbest (custom) attribute değeri — id'siz metin.</summary>
    public const int CustomAttributeValueMaxLength = 2000;

    // Kanal-özel varyant ÖZELLİĞİ/DEĞERİ (SalesChannelTrTrendyolProductAttribute/Value) — ERP ProductAttributeConsts
    // ve N11ProductConsts ile HİZALI (klon-sonra-ayrış deseni; aynı alan sınırları).
    public const int AttributeNameMaxLength = 64;    // ör. "Renk", "Beden"
    public const int AttributeValueMaxLength = 128;  // ör. "Kırmızı", "Siyah"

    /// <summary>Kartezyen kombinasyon imzası ("{AttributeId}={ValueId}|...") üst sınırı — makul özellik sayısı × Guid uzunluğu.</summary>
    public const int CombinationSignatureMaxLength = 600;
}
