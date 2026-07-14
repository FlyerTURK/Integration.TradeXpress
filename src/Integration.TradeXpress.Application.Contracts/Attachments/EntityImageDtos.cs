using System;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Attachments;

/// <summary>Entity-agnostik görsel — okuma DTO'su (GetForAsync). Önizleme (data-URL) sunucu doldurur.</summary>
public class EntityImageDto
{
    public Guid Id { get; set; }
    public ProductImageSourceType SourceType { get; set; }
    public string? Url { get; set; }
    public string? BlobName { get; set; }
    public string? FileName { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }

    /// <summary>Önizleme (data-URL ya da dış URL) — grid/form gösterimi; save yoksayar.</summary>
    public string? PreviewDataUrl { get; set; }
}

/// <summary>Entity-agnostik görsel düzenleme düğümü (drill editör + ReplaceForAsync girdisi) — paylaşılan
/// <c>SingleImageEditFields</c> sözleşmesi (<see cref="ISingleImageEditModel"/>).</summary>
public class EntityImageEditDto : ISingleImageEditModel
{
    /// <summary>İstemci-tarafı satır anahtarı (@key) — kalıcı değil.</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    public ProductImageSourceType SourceType { get; set; }
    public string? Url { get; set; }
    public string? BlobName { get; set; }
    public string? FileName { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }

    /// <summary>Önizleme (data-URL) — SALT görüntü; upload/GetFor doldurur, save yoksayar.</summary>
    public string? PreviewDataUrl { get; set; }
}

/// <summary>Görsel yükleme isteği (dosya adı + içerik) — blob'a kaydeder, önizleme döner. Henüz bir entity'ye
/// BAĞLANMAZ (parent save'de ReplaceForAsync bağlar).</summary>
public class EntityImageUploadDto
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

/// <summary>Yükleme sonucu — blob adı + önizleme data-URL.</summary>
public class EntityImageUploadResultDto
{
    public string BlobName { get; set; } = string.Empty;
    public string? PreviewDataUrl { get; set; }
}
