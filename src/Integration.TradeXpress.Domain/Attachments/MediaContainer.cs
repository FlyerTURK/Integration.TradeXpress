using Volo.Abp.BlobStoring;

namespace Integration.TradeXpress.Attachments;

/// <summary>Merkezi medya blob container'ı — görsel/video içerikleri + poster kareleri TEK container'da (Database provider,
/// ABP AppBlobs). Self-contained: URL-import de dahil tüm medya içeriği buraya indirilir/yazılır.</summary>
[BlobContainerName("media")]
public class MediaContainer
{
}
