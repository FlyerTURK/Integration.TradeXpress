using System;
using System.Collections.Generic;
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
/// eşleştirme (<c>OrderAppService.SaveOrderLineEditAsync</c>) kullanır (DRY). Görsel VARYANT-FARKINDA fallback zinciriyle
/// seçilir (önce varyanta özel, sonra ürün-geneli, en son herhangi biri — detay <c>SelectImage</c>); Url tipi doğrudan,
/// Upload tipi THUMBNAIL blob'undan data-URL (<see cref="ProductImageAppService"/> ile AYNI desen; tam çözünürlük hiç gömülmez).
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
        var image = SelectImage(product, variant.Id);
        var imageUrl = await ResolveImageUrlAsync(image);
        return (variant.Name, imageUrl);
    }

    /// <summary>
    /// Varyant-farkında görsel seçimi (fallback zinciri). GEREKÇE: satır belirli bir varyanta eşleşti; ad varyantınki
    /// donarken görselin ürün-düzeyi default'tan gelmesi yanlış SKU'yu gösterir (ör. Mavi yüzük satırına Kırmızı thumbnail).
    /// Zincir: (1) varyanta özel görseller (<see cref="ProductImage.VariantId"/> eşleşen) → (2) ürün-geneli görseller
    /// (VariantId == null) → (3) herhangi bir görsel (eski davranış — yalnız başka varyanta bağlı görseller kaldıysa bile
    /// boş yerine bir şey göster). variantId null ise (eşleşmemiş/jenerik çağrı) doğrudan (2)→(3).
    /// Her adımda tercih: IsDefault işaretli önce, yoksa DisplayOrder'a göre ilk.
    /// </summary>
    private static ProductImage? SelectImage(Product? product, Guid? variantId)
    {
        if (product is null || product.Images.Count == 0)
        {
            return null;
        }

        if (variantId is { } id)
        {
            var variantImage = PickPreferred(product.Images.Where(i => i.VariantId == id));
            if (variantImage is not null)
            {
                return variantImage;
            }
        }

        var sharedImage = PickPreferred(product.Images.Where(i => i.VariantId == null));
        if (sharedImage is not null)
        {
            return sharedImage;
        }

        return PickPreferred(product.Images);
    }

    /// <summary>Aday kümeden tercih edilen görsel: IsDefault işaretli olan önce, yoksa DisplayOrder'a göre ilk.</summary>
    private static ProductImage? PickPreferred(IEnumerable<ProductImage> candidates)
    {
        // Owned JSON koleksiyonu bellekte — çift numaralandırma ucuz; ara liste kurmaya gerek yok.
        return candidates.FirstOrDefault(i => i.IsDefault)
            ?? candidates.OrderBy(i => i.DisplayOrder).FirstOrDefault();
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
