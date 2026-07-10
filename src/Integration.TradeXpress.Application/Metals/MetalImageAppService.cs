using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.BlobStoring;

namespace Integration.TradeXpress.Metals;

/// <summary>
/// Maden görseli dosya servisi (Product deseni) — blob storage (Database provider, <see cref="MetalImagesContainer"/>).
/// Guard'lar + thumbnail çekirdeği <see cref="ImageUploadPipeline"/>'da (Product ile ORTAK — DRY); önizlemeler
/// hep thumbnail'den servis edilir, tam içerik DTO'ya gömülmez.
/// </summary>
[Authorize(TradeXpressPermissions.Metals.Default)]
public class MetalImageAppService : TradeXpressAppService, IMetalImageAppService
{
    private const string ErrorCodePrefix = "TradeXpress:Metal";

    private readonly IBlobContainer<MetalImagesContainer> _container;

    public MetalImageAppService(IBlobContainer<MetalImagesContainer> container)
    {
        _container = container;
    }

    public virtual async Task<MetalImageUploadResultDto> UploadAsync(MetalImageUploadDto input)
    {
        // Yetki (Create VEYA Update) + guard'lar + thumbnail + blob kaydı ORTAK çekirdekte (Product ile aynı akış — DRY).
        await ImageUploadPipeline.EnsureCanUploadAsync(
            AuthorizationService, TradeXpressPermissions.Metals.Create, TradeXpressPermissions.Metals.Update);

        var uploaded = await ImageUploadPipeline.UploadAsync(
            _container, GuidGenerator, input.FileName, input.Content, MetalConsts.MaxImageSizeBytes, ErrorCodePrefix);

        return new MetalImageUploadResultDto
        {
            BlobName = uploaded.BlobName,
            PreviewDataUrl = uploaded.PreviewDataUrl,
        };
    }
}
