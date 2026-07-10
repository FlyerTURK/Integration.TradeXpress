using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Metals;

/// <summary>Maden temsili görseli — owned (Metal.Image → JSON kolonu), TEK görsel (katalog kaydına bir temsili
/// görsel yeter — 2026-07-10 ürün kararı; DisplayOrder/IsDefault YOK). İki kaynak: dış URL ya da yüklenmiş dosya
/// (blob storage; <see cref="BlobName"/>). Kaynak tipi Product ile ORTAK (<see cref="ProductImageSourceType"/>).</summary>
public class MetalImage
{
    #region Constructors

    public MetalImage()
    {
    }

    public MetalImage(ProductImageSourceType sourceType, string? url, string? blobName, string? fileName)
    {
        SourceType = sourceType;
        Url = url;
        BlobName = blobName;
        FileName = fileName;
    }

    #endregion

    #region Properties

    public ProductImageSourceType SourceType { get; set; }

    /// <summary>Dış görsel bağlantısı — yalnız <see cref="ProductImageSourceType.Url"/> kaynağında dolu.</summary>
    public string? Url { get; set; }

    /// <summary>Blob adı (Guid + uzantı) — yalnız <see cref="ProductImageSourceType.Upload"/> kaynağında dolu.</summary>
    public string? BlobName { get; set; }

    /// <summary>Yüklenen dosyanın orijinal adı (görüntü/teşhis) — Upload kaynağında dolu.</summary>
    public string? FileName { get; set; }

    #endregion

    #region Methods

    /// <summary>Kaynağı gerçekten dolu mu — Url tipinde URL, Upload tipinde blob adı ister (bilinmeyen tip = boş).</summary>
    public bool HasSource()
    {
        if (SourceType == ProductImageSourceType.Url)
        {
            return !string.IsNullOrWhiteSpace(Url);
        }

        if (SourceType == ProductImageSourceType.Upload)
        {
            return !string.IsNullOrWhiteSpace(BlobName);
        }

        return false;
    }

    #endregion
}
