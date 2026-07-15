using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Variants;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Bir <see cref="EntityVariant"/>'ın (Product uzantısı; EntityName="Product") "eşleşme anı" görünümünü (isim + görsel)
/// donduran PAYLAŞILAN yardımcı — hem otomatik eşleştirme (<c>OrderLineProductMatcher</c>, sync sırasında) hem manuel
/// eşleştirme (<c>OrderAppService.SaveOrderLineEditAsync</c>) kullanır (DRY). Görsel: Product.Images'taki varsayılan görsel —
/// Url tipi doğrudan, Upload tipi THUMBNAIL blob'undan data-URL (<see cref="ProductImageAppService"/> ile AYNI desen;
/// tam çözünürlük hiç gömülmez).
/// </summary>
public class OrderLineProductSnapshotBuilder : ITransientDependency
{
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IBlobContainer<ProductImagesContainer> _imageContainer;

    public OrderLineProductSnapshotBuilder(
        IRepository<Product, Guid> productRepository,
        IBlobContainer<ProductImagesContainer> imageContainer)
    {
        _productRepository = productRepository;
        _imageContainer = imageContainer;
    }

    /// <summary>Varyantın o ANDAKİ isim + görselini döner (isim her zaman dolu; görsel yoksa null). Jenerik
    /// <see cref="EntityVariant"/> — sahip ürün Id'si <see cref="EntityVariant.EntityId"/>'de (EntityName="Product").</summary>
    public async Task<(string Name, string? ImageUrl)> BuildAsync(EntityVariant variant)
    {
        var product = await _productRepository.FindAsync(variant.EntityId);
        var image = product?.Images.FirstOrDefault(i => i.IsDefault)
            ?? product?.Images.OrderBy(i => i.DisplayOrder).FirstOrDefault();
        var imageUrl = await ResolveImageUrlAsync(image);
        return (variant.Name, imageUrl);
    }

    private async Task<string?> ResolveImageUrlAsync(ProductImage? image)
    {
        if (image is null)
        {
            return null;
        }

        if (image.SourceType == ProductImageSourceType.Url)
        {
            return string.IsNullOrEmpty(image.Url) ? null : image.Url;
        }

        if (string.IsNullOrEmpty(image.BlobName))
        {
            return null;
        }

        var thumbnail = await _imageContainer.GetAllBytesOrNullAsync(ProductImageAppService.ThumbnailNameOf(image.BlobName));
        return thumbnail is null ? null : ProductImageAppService.BuildPreviewDataUrl(thumbnail);
    }
}
