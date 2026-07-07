using Volo.Abp.BlobStoring;

namespace Integration.TradeXpress.Products;

/// <summary>Ürün görsel dosyalarının blob konteyneri — Database provider'da saklanır (module config).
/// Blob adı: Guid + orijinal uzantı (tahmin edilemez; içerik yalnız authorized AppService'ten okunur).</summary>
[BlobContainerName("product-images")]
public class ProductImagesContainer
{
}
