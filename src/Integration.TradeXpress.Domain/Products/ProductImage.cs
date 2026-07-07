namespace Integration.TradeXpress.Products;

/// <summary>Ürün görseli — owned (Product.Images → JSON kolonu). İki kaynak: dış URL ya da yüklenmiş dosya
/// (blob storage; <see cref="BlobName"/>). Sıra <see cref="DisplayOrder"/> ile (küçük önce; ilk = ana görsel).</summary>
public class ProductImage
{
    public ProductImage()
    {
    }

    public ProductImage(ProductImageSourceType sourceType, string? url, string? blobName, string? fileName, int displayOrder)
    {
        SourceType = sourceType;
        Url = url;
        BlobName = blobName;
        FileName = fileName;
        DisplayOrder = displayOrder;
    }

    public ProductImageSourceType SourceType { get; set; }

    /// <summary>Dış görsel bağlantısı — yalnız <see cref="ProductImageSourceType.Url"/> kaynağında dolu.</summary>
    public string? Url { get; set; }

    /// <summary>Blob adı (Guid + uzantı) — yalnız <see cref="ProductImageSourceType.Upload"/> kaynağında dolu.</summary>
    public string? BlobName { get; set; }

    /// <summary>Yüklenen dosyanın orijinal adı (görüntü/teşhis) — Upload kaynağında dolu.</summary>
    public string? FileName { get; set; }

    public int DisplayOrder { get; set; }
}
