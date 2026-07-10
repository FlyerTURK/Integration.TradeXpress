using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.BlobStoring;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürün görseli dosya servisi — blob storage (Database provider, <see cref="ProductImagesContainer"/>).
/// Guard'lar: boyut (<see cref="ProductConsts.MaxImageSizeBytes"/>) + uzantı whitelist. Blob adı Guid + uzantı.
/// Upload anında AYRICA küçük JPEG <b>thumbnail</b> üretilir ("thumb-{blobName}.jpg") — önizlemeler (grid/form/GetDto)
/// HEP thumbnail'den servis edilir: tam içerik DTO'ya/DOM'a gömülmez (4MB×8 base64 şişmesi + her render dirty-check
/// JSON maliyeti review'da kanıtlanan form kilidiydi). Guard/thumbnail çekirdeği <see cref="ImageUploadPipeline"/>'da
/// (Metal görselleriyle ORTAK — DRY).
/// </summary>
[Authorize(TradeXpressPermissions.Products.Default)]
public class ProductImageAppService : TradeXpressAppService, IProductImageAppService
{
    private const string ErrorCodePrefix = "TradeXpress:Product";

    private readonly IBlobContainer<ProductImagesContainer> _container;

    public ProductImageAppService(IBlobContainer<ProductImagesContainer> container)
    {
        _container = container;
    }

    public virtual async Task<ProductImageUploadResultDto> UploadAsync(ProductImageUploadDto input)
    {
        // Yetki + guard'lar + thumbnail + blob kaydı ORTAK çekirdekte (Metal ile aynı akış — DRY).
        await ImageUploadPipeline.EnsureCanUploadAsync(
            AuthorizationService, TradeXpressPermissions.Products.Create, TradeXpressPermissions.Products.Update);

        var uploaded = await ImageUploadPipeline.UploadAsync(
            _container, GuidGenerator, input.FileName, input.Content, ProductConsts.MaxImageSizeBytes, ErrorCodePrefix);

        return new ProductImageUploadResultDto
        {
            BlobName = uploaded.BlobName,
            PreviewDataUrl = uploaded.PreviewDataUrl,
        };
    }

    /// <summary>Thumbnail blob adı — <see cref="ImageUploadPipeline.ThumbnailNameOf"/> delegesi
    /// (mevcut çağrı yerleri için korunan API; kural TEK yerde).</summary>
    public static string ThumbnailNameOf(string blobName)
    {
        return ImageUploadPipeline.ThumbnailNameOf(blobName);
    }

    /// <summary>Önizleme data-URL'i — <see cref="ImageUploadPipeline.BuildPreviewDataUrl"/> delegesi.</summary>
    public static string BuildPreviewDataUrl(byte[] thumbnailContent)
    {
        return ImageUploadPipeline.BuildPreviewDataUrl(thumbnailContent);
    }
}
