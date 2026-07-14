using System;

namespace Integration.TradeXpress.Attachments;

/// <summary>Entity-agnostik doküman — okuma DTO'su (GetForAsync). İçerik dönmez; indirme <c>DownloadAsync(Id)</c> ile.</summary>
public class EntityDocumentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
}

/// <summary>Entity-agnostik doküman düzenleme düğümü (drill editör + ReplaceForAsync girdisi). Kaydedilmiş kayıtların
/// <see cref="Id"/>'si dolu gelir (indirme bunu kullanır); yeni-yüklenip-henüz-kaydedilmemiş satırlarda boştur.</summary>
public class EntityDocumentEditDto
{
    /// <summary>İstemci-tarafı satır anahtarı (@key) — kalıcı değil.</summary>
    public Guid ClientKey { get; set; } = Guid.NewGuid();

    /// <summary>Kaydedilmiş dokümanın kalıcı Id'si — indirme (<c>DownloadAsync</c>) için. Yeni satırda boş.</summary>
    public Guid Id { get; set; }

    public string? FileName { get; set; }
    public string? BlobName { get; set; }
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
}

/// <summary>Doküman yükleme isteği (dosya adı + içerik) — blob'a kaydeder. Henüz bir entity'ye BAĞLANMAZ
/// (parent save'de ReplaceForAsync bağlar).</summary>
public class EntityDocumentUploadDto
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

/// <summary>Yükleme sonucu — blob adı + türetilen MIME + boyut (edit satırına yazılır).</summary>
public class EntityDocumentUploadResultDto
{
    public string BlobName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
}

/// <summary>İndirme yanıtı — blob içeriği + tarayıcıya sunulacak dosya adı ve MIME tipi.</summary>
public class EntityDocumentDownloadDto
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
}
