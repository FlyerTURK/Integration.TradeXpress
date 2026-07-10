namespace Integration.TradeXpress.Products;

/// <summary>
/// TEK görsel düzenleme modeli — paylaşılan <c>SingleImageEditFields</c> bileşeninin bağlandığı sözleşme
/// (SubAccount'un <c>ISubAccountEditableFields</c> deseni). Ürün görsel graf düğümü (çok-görselli drill) ve
/// maden görsel DTO'su (tek görsel) bunu implement eder; bileşen kaynak tipi / URL / dosya / önizleme
/// alanlarını buradan okur-yazar.
/// </summary>
public interface ISingleImageEditModel
{
    ProductImageSourceType SourceType { get; set; }

    /// <summary>Dış görsel bağlantısı — yalnız <see cref="ProductImageSourceType.Url"/> kaynağında dolu.</summary>
    string? Url { get; set; }

    /// <summary>Blob adı — yalnız <see cref="ProductImageSourceType.Upload"/> kaynağında dolu.</summary>
    string? BlobName { get; set; }

    /// <summary>Yüklenen dosyanın orijinal adı (görüntü/teşhis).</summary>
    string? FileName { get; set; }

    /// <summary>Blob görselin önizlemesi (data URL) — SALT görüntü; sunucu/upload doldurur, save yoksayar.</summary>
    string? PreviewDataUrl { get; set; }
}
