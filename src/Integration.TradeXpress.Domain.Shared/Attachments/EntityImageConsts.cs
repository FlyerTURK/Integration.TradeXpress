namespace Integration.TradeXpress.Attachments;

/// <summary>Entity-agnostik görsel (EntityImage — herhangi bir entity'ye EntityName+EntityId ile bağlı görsel)
/// alan sınırları. Product/Metal görsel sabitleriyle hizalı.</summary>
public static class EntityImageConsts
{
    public const int EntityNameMaxLength  = 128;   // teknik: sahip entity tipi adı (ör. "Good", "GoodVariant")
    public const int UrlMaxLength         = 1000;
    public const int BlobNameMaxLength    = 64;
    public const int FileNameMaxLength    = 256;

    /// <summary>Tek yükleme boyut sınırı (4 MB) — ImageUploadPipeline guard'ı.</summary>
    public const int MaxImageSizeBytes = 4 * 1024 * 1024;
}
