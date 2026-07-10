using Volo.Abp.BlobStoring;

namespace Integration.TradeXpress.Metals;

/// <summary>Maden görsel dosyalarının blob konteyneri — Database provider'da saklanır (module config,
/// <c>ProductImagesContainer</c> ile aynı bağlama). Blob adı: Guid + orijinal uzantı (tahmin edilemez;
/// içerik yalnız authorized AppService'ten okunur).</summary>
[BlobContainerName("metal-images")]
public class MetalImagesContainer
{
}
