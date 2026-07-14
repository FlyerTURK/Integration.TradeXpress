namespace Integration.TradeXpress.Attachments;

/// <summary>Entity-agnostik doküman (EntityDocument — herhangi bir entity'ye EntityName+EntityId ile bağlı blob
/// dosya eki) alan sınırları. EntityImage sabitleriyle hizalı; görsel-özel Url alanı YOK (doküman her zaman
/// yüklenmiş dosya).</summary>
public static class EntityDocumentConsts
{
    public const int EntityNameMaxLength  = 128;   // teknik: sahip entity tipi adı (ör. "Good", "GoodVariant")
    public const int FileNameMaxLength    = 256;   // yüklenen dosyanın orijinal adı
    public const int BlobNameMaxLength    = 64;    // blob adı (Guid "N" + uzantı)
    public const int ContentTypeMaxLength = 128;   // MIME tipi (ör. "application/pdf")
    public const int DescriptionMaxLength = 512;   // opsiyonel açıklama/etiket

    /// <summary>Tek yükleme boyut sınırı (20 MB) — upload guard'ı.</summary>
    public const int MaxDocumentSizeBytes = 20 * 1024 * 1024;
}
