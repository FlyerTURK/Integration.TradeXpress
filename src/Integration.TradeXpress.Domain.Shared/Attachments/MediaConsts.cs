namespace Integration.TradeXpress.Attachments;

/// <summary>Merkezi medya kütüphanesi (Media = görsel/video, self-contained blob) + entity-medya linki alan sınırları.</summary>
public static class MediaConsts
{
    public const int FileNameMaxLength    = 256;
    public const int BlobNameMaxLength    = 64;    // Guid "N" (+ opsiyonel uzantı)
    public const int ContentTypeMaxLength = 128;   // MIME (ör. image/jpeg, video/mp4)
    public const int ContentHashMaxLength = 64;    // SHA-256 hex → dedup anahtarı
    public const int EntityNameMaxLength  = 128;   // link: sahip entity tipi adı (ör. "Good", "GoodVariant")
    public const int FolderNameMaxLength  = 128;   // kütüphane klasörü adı

    /// <summary>Görsel tek-yükleme/-import boyut sınırı (10 MB) — legacy ProductImage yoluyla PARİTE
    /// (2026-08-01 denetim bulgusu: DAM'ın eski 4 MB tavanı, pazaryeri importunun 4-10 MB fotoğrafları
    /// sessizce atlamasına yol açıyordu; Etsy il_fullxfull görselleri rutin olarak 4 MB'ı aşar).
    /// Blazor circuit 16 MB mesaj sınırının güvenli altında; ImageSharp decompression-bomb guard'ı ayrıca devrede.</summary>
    public const long MaxImageSizeBytes = 10L * 1024 * 1024;

    /// <summary>Video tek-yükleme/-import boyut sınırı (100 MB) — blob'a self-contained saklanır.</summary>
    public const long MaxVideoSizeBytes = 100L * 1024 * 1024;
}
