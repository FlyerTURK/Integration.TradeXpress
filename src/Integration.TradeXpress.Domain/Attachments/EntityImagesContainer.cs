using Volo.Abp.BlobStoring;

namespace Integration.TradeXpress.Attachments;

/// <summary>Entity-agnostik görsel blob container'ı — TÜM entity'lerin (Good/GoodVariant/Product/Metal…) görselleri
/// TEK container'da (Product/Metal'in ayrı container'larının yerini alır; PublicImageLinkProvider buraya bağlanır).
/// Database provider (ABP AppBlobs).</summary>
[BlobContainerName("entity-images")]
public class EntityImagesContainer
{
}
