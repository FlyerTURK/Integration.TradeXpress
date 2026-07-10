using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Metals;

/// <summary>Metal (Maden) alan sınırları (Scrap/VoucherConsts ile hizalı).</summary>
public static class MetalConsts
{
    public const int CodeMaxLength        = 16;
    public const int NameMaxLength        = 128;
    public const int DescriptionMaxLength = 512;
    public const int BarcodeMaxLength     = 64;

    // Görsel alan/yükleme sınırları — Product görsel altyapısıyla AYNI (SSOT: ProductConsts; ayrı sayı tutma).
    public const int ImageUrlMaxLength      = ProductConsts.ImageUrlMaxLength;
    public const int ImageBlobNameMaxLength = ProductConsts.ImageBlobNameMaxLength;
    public const int ImageFileNameMaxLength = ProductConsts.ImageFileNameMaxLength;
    public const int MaxImageSizeBytes      = ProductConsts.MaxImageSizeBytes;

    // Factor (milyem; gram-altı ≤1, sikke >1) ve işçilik/StableQuantity — N5.
    public const int DecimalPrecision = 18;
    public const int DecimalScale     = 5;

    public const decimal DefaultFactor = 0.995m;
}
