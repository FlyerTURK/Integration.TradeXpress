using Volo.Abp.BlobStoring;

namespace Integration.TradeXpress.Attachments;

/// <summary>Entity-agnostik doküman blob container'ı — TÜM entity'lerin (Good/GoodVariant/…) yüklenmiş dokümanları
/// TEK container'da. Database provider (ABP AppBlobs). Görsel container'ından (<see cref="EntityImagesContainer"/>)
/// ayrıdır (doküman ham blob; thumbnail/pipeline yok).</summary>
[BlobContainerName("entity-documents")]
public class EntityDocumentsContainer
{
}
