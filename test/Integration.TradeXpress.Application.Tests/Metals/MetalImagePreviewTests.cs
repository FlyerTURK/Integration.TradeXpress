using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Products;
using Shouldly;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Metals;

/// <summary>
/// Maden görseli round-trip doğrulaması (Product ProductImagePreviewTests deseni) — gerçek Sqlite DB ile:
/// <c>Metal.Image</c> owned JSON kolonu persist/materialize oluyor mu, <c>GetAsync</c> upload önizlemesini
/// (thumbnail data-URL) dolduruyor mu, <c>GetListAsync</c> ImagePreviewUrl'i (Url → direkt link, Upload →
/// thumbnail) yazıyor mu ve görsel kaldırılınca yetim blob temizleniyor mu (tahmin değil kanıt).
/// </summary>
public abstract class MetalImagePreviewTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    // 1x1 şeffaf PNG — ImageSharp thumbnail üretimi gerçek bir görsel ister.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly IMetalAppService _metalAppService;
    private readonly IMetalImageAppService _imageAppService;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IBlobContainer<MetalImagesContainer> _imageContainer;

    protected MetalImagePreviewTests()
    {
        _metalAppService = GetRequiredService<IMetalAppService>();
        _imageAppService = GetRequiredService<IMetalImageAppService>();
        _unitRepository = GetRequiredService<IRepository<CurrencyUnit, Guid>>();
        _imageContainer = GetRequiredService<IBlobContainer<MetalImagesContainer>>();
    }

    [Fact]
    public async Task Image_roundtrips_and_previews_fill_for_url_upload_and_stay_null_without_image()
    {
        var unitId = await GetAnyUnitIdAsync();

        var uploadResult = await _imageAppService.UploadAsync(new MetalImageUploadDto
        {
            FileName = "test.png",
            Content = TinyPng,
        });

        var uploadMetal = await _metalAppService.CreateAsync(new MetalCreateDto
        {
            Code = "IMGUP",
            Name = "Upload Görselli Maden",
            FollowingUnitId = unitId,
            Image = new MetalImageDto
            {
                SourceType = ProductImageSourceType.Upload,
                BlobName = uploadResult.BlobName,
                FileName = "test.png",
            },
        });

        var urlMetal = await _metalAppService.CreateAsync(new MetalCreateDto
        {
            Code = "IMGURL",
            Name = "Url Görselli Maden",
            FollowingUnitId = unitId,
            Image = new MetalImageDto
            {
                SourceType = ProductImageSourceType.Url,
                Url = "https://example.com/pic.jpg",
            },
        });

        var bareMetal = await _metalAppService.CreateAsync(new MetalCreateDto
        {
            Code = "IMGNONE",
            Name = "Görselsiz Maden",
            FollowingUnitId = unitId,
        });

        // GetAsync — JSON kolonu round-trip + upload'da thumbnail data-URL önizlemesi.
        var uploadDto = await _metalAppService.GetAsync(uploadMetal.Id);
        uploadDto.Image.ShouldNotBeNull();
        uploadDto.Image!.SourceType.ShouldBe(ProductImageSourceType.Upload);
        uploadDto.Image.BlobName.ShouldBe(uploadResult.BlobName);
        uploadDto.Image.PreviewDataUrl.ShouldBe(uploadResult.PreviewDataUrl);

        var urlDto = await _metalAppService.GetAsync(urlMetal.Id);
        urlDto.Image.ShouldNotBeNull();
        urlDto.Image!.SourceType.ShouldBe(ProductImageSourceType.Url);
        urlDto.Image.Url.ShouldBe("https://example.com/pic.jpg");

        var bareDto = await _metalAppService.GetAsync(bareMetal.Id);
        bareDto.Image.ShouldNotBeNull();          // client binding için boş model garanti edilir
        bareDto.Image!.Url.ShouldBeNull();
        bareDto.Image.BlobName.ShouldBeNull();

        // GetListAsync — grid önizleme kolonu.
        var list = await _metalAppService.GetListAsync(new MetalListRequestDto { MaxResultCount = 1000 });
        list.Items.Single(x => x.Id == uploadMetal.Id).ImagePreviewUrl.ShouldBe(uploadResult.PreviewDataUrl);
        list.Items.Single(x => x.Id == urlMetal.Id).ImagePreviewUrl.ShouldBe("https://example.com/pic.jpg");
        list.Items.Single(x => x.Id == bareMetal.Id).ImagePreviewUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Removing_uploaded_image_on_update_deletes_orphan_blobs()
    {
        var unitId = await GetAnyUnitIdAsync();

        var uploadResult = await _imageAppService.UploadAsync(new MetalImageUploadDto
        {
            FileName = "orphan.png",
            Content = TinyPng,
        });

        var created = await _metalAppService.CreateAsync(new MetalCreateDto
        {
            Code = "IMGORP",
            Name = "Yetim Blob Madeni",
            FollowingUnitId = unitId,
            Image = new MetalImageDto
            {
                SourceType = ProductImageSourceType.Upload,
                BlobName = uploadResult.BlobName,
                FileName = "orphan.png",
            },
        });

        (await _imageContainer.GetAllBytesOrNullAsync(uploadResult.BlobName)).ShouldNotBeNull();

        // Görsel kaldırılıyor (kaynağı boş model) → entity JSON'u temizlenir + blob ve thumbnail'i silinir.
        var update = new MetalUpdateDto
        {
            Code = created.Code,
            Name = created.Name,
            FollowingUnitId = unitId,
            Factor = created.Factor,
            IsActive = created.IsActive,
            Image = new MetalImageDto { SourceType = ProductImageSourceType.Upload },
        };
        var updated = await _metalAppService.UpdateAsync(created.Id, update);

        updated.Image.ShouldNotBeNull();
        updated.Image!.BlobName.ShouldBeNull();
        (await _imageContainer.GetAllBytesOrNullAsync(uploadResult.BlobName)).ShouldBeNull();
    }

    /// <summary>Seed'lenmiş herhangi bir para birimi (CurrencyUnitSeeder host kataloğu) — FollowingUnit ZORUNLU.</summary>
    private async Task<Guid> GetAnyUnitIdAsync()
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var units = await _unitRepository.GetListAsync();
            return units.First().Id;
        });
    }
}
