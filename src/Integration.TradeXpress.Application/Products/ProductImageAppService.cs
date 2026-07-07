using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.BlobStoring;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürün görseli dosya servisi — blob storage (Database provider, <see cref="ProductImagesContainer"/>).
/// Guard'lar: boyut (<see cref="ProductConsts.MaxImageSizeBytes"/>) + uzantı whitelist. Blob adı Guid + uzantı.
/// Upload anında AYRICA küçük JPEG <b>thumbnail</b> üretilir ("thumb-{blobName}.jpg") — önizlemeler (grid/form/GetDto)
/// HEP thumbnail'den servis edilir: tam içerik DTO'ya/DOM'a gömülmez (4MB×8 base64 şişmesi + her render dirty-check
/// JSON maliyeti review'da kanıtlanan form kilidiydi).
/// </summary>
[Authorize(TradeXpressPermissions.Products.Default)]
public class ProductImageAppService : TradeXpressAppService, IProductImageAppService
{
    private const int ThumbnailMaxEdge = 240;   // önizleme uzun kenarı (px)

    private readonly IBlobContainer<ProductImagesContainer> _container;

    public ProductImageAppService(IBlobContainer<ProductImagesContainer> container)
    {
        _container = container;
    }

    public virtual async Task<ProductImageUploadResultDto> UploadAsync(ProductImageUploadDto input)
    {
        // Create VEYA Update yeterli (yeni ürün oluştururken de yüklenir; yalnız-Create'li kullanıcı takılmasın).
        await EnsureCanUploadAsync();

        if (input.Content.Length == 0)
        {
            throw new BusinessException("TradeXpress:Product:ImageEmpty");
        }

        if (input.Content.Length > ProductConsts.MaxImageSizeBytes)
        {
            throw new BusinessException("TradeXpress:Product:ImageTooLarge")
                .WithData("MaxMb", ProductConsts.MaxImageSizeBytes / (1024 * 1024));
        }

        var extension = Path.GetExtension(input.FileName).ToLowerInvariant();
        if (!ContentTypes.ContainsKey(extension))
        {
            throw new BusinessException("TradeXpress:Product:ImageTypeNotSupported");
        }

        var thumbnail = BuildThumbnail(input.Content);

        var blobName = GuidGenerator.Create().ToString("N") + extension;
        await _container.SaveAsync(blobName, input.Content);
        await _container.SaveAsync(ThumbnailNameOf(blobName), thumbnail);

        return new ProductImageUploadResultDto
        {
            BlobName = blobName,
            PreviewDataUrl = BuildPreviewDataUrl(thumbnail),
        };
    }

    /// <summary>Thumbnail blob adı — ana blob'dan türetilir (silme/okuma tek kuraldan).</summary>
    public static string ThumbnailNameOf(string blobName)
    {
        return "thumb-" + blobName + ".jpg";
    }

    /// <summary>Thumbnail JPEG içeriğinden önizleme data-URL'i.</summary>
    public static string BuildPreviewDataUrl(byte[] thumbnailContent)
    {
        return "data:image/jpeg;base64," + Convert.ToBase64String(thumbnailContent);
    }

    /// <summary>Görseli en-boy oranını koruyarak küçültür (uzun kenar <see cref="ThumbnailMaxEdge"/> px) → JPEG.
    /// Bozuk/görsel-olmayan içerik dostane hatayla reddedilir (whitelist'i geçen ama gerçek görsel olmayan dosya).</summary>
    private static byte[] BuildThumbnail(byte[] content)
    {
        try
        {
            using var image = Image.Load(content);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(ThumbnailMaxEdge, ThumbnailMaxEdge),
            }));

            using var output = new MemoryStream();
            image.SaveAsJpeg(output, new JpegEncoder { Quality = 80 });
            return output.ToArray();
        }
        catch (Exception ex) when (ex is not BusinessException)
        {
            throw new BusinessException("TradeXpress:Product:ImageTypeNotSupported");
        }
    }

    /// <summary>Upload yetkisi: Products.Create YA DA Products.Update (attribute tek izne kilitliyordu — review bulgusu).</summary>
    private async Task EnsureCanUploadAsync()
    {
        if (await AuthorizationService.IsGrantedAsync(TradeXpressPermissions.Products.Create)
            || await AuthorizationService.IsGrantedAsync(TradeXpressPermissions.Products.Update))
        {
            return;
        }

        throw new AbpAuthorizationException(code: TradeXpressPermissions.Products.Update);
    }

    // İzinli görsel türleri (uzantı → mime). Whitelist — başka tür yüklemesi dostane hatayla reddedilir.
    private static readonly Dictionary<string, string> ContentTypes = new()
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
    };
}
