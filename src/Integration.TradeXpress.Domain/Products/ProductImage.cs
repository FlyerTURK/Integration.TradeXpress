namespace Integration.TradeXpress.Products;

/// <summary>Ürün görseli — owned (Product.Images → JSON kolonu). İki kaynak: dış URL ya da yüklenmiş dosya
/// (blob storage; <see cref="BlobName"/>). Sıra <see cref="DisplayOrder"/> ile (küçük önce; ilk = ana görsel).</summary>
public class ProductImage
{
    public ProductImage()
    {
    }

    public ProductImage(ProductImageSourceType sourceType, string? url, string? blobName, string? fileName, int displayOrder, bool isDefault, Guid? variantId, string? variantCode)
    {
        SourceType = sourceType;
        Url = url;
        BlobName = blobName;
        FileName = fileName;
        DisplayOrder = displayOrder;
        IsDefault = isDefault;
        VariantId = variantId;
        VariantCode = variantCode;
    }

    public ProductImageSourceType SourceType { get; set; }

    /// <summary>Dış görsel bağlantısı — yalnız <see cref="ProductImageSourceType.Url"/> kaynağında dolu.</summary>
    public string? Url { get; set; }

    /// <summary>Blob adı (Guid + uzantı) — yalnız <see cref="ProductImageSourceType.Upload"/> kaynağında dolu.</summary>
    public string? BlobName { get; set; }

    /// <summary>Yüklenen dosyanın orijinal adı (görüntü/teşhis) — Upload kaynağında dolu.</summary>
    public string? FileName { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>Görselin bağlı olduğu VARYANT (null = ürün-geneli görsel; tüm varyantlara ortak).</summary>
    public Guid? VariantId { get; set; }

    /// <summary>Varyant kodu (denormalize — blob path'i + gösterim için; <see cref="FileName"/> gibi taşınır).
    /// null/boş = ürün-geneli.</summary>
    public string? VariantCode { get; set; }

    /// <summary>Varsayılan (ana) görsel — marketplace push'unda İLK sıraya alınır. Tekil-default garantisi
    /// <c>Product.SetImages</c>'ta (birden fazla işaretliyse ilki kalır; hiç yoksa ilk görsel default olur).</summary>
    public bool IsDefault { get; set; }
}
