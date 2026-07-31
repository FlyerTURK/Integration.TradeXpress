using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürün liste önizlemesinin MERKEZİ DAM'dan geldiğini kilitler (legacy ProductImage 2026-07-31'de emekli).
/// Önizleme <c>GetDefaultPosterMapAsync</c> batch'inden dolar: kapaklı üründe kapağın poster'ı, medyasız
/// üründe null. Kırılırsa grid önizlemesi sessizce boşalır — istisna fırlamaz, kullanıcı görselleri "kayboldu" görür.
/// </summary>
public abstract class ProductMediaPreviewTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IProductAppService _appService;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<Media, Guid> _mediaRepository;
    private readonly IRepository<EntityMediaLink, Guid> _linkRepository;
    private readonly ICurrentCompany _currentCompany;

    protected ProductMediaPreviewTests()
    {
        _appService = GetRequiredService<IProductAppService>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _mediaRepository = GetRequiredService<IRepository<Media, Guid>>();
        _linkRepository = GetRequiredService<IRepository<EntityMediaLink, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task GetList_fills_preview_from_dam_cover_and_leaves_medialess_null()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var withMedia = await SeedProductAsync(companyId, "PRMEDIA1");
            var withoutMedia = await SeedProductAsync(companyId, "PRMEDIA2");

            var mediaId = await WithUnitOfWorkAsync(async () =>
            {
                // PosterUrl yalnız PosterBlobName doluysa üretilir (upload boru hattı doldurur; burada elle).
                var seeded = new Media(
                    companyId,
                    MediaType.Image,
                    blobName: Guid.NewGuid().ToString("N"),
                    fileName: "cover.jpg",
                    contentType: "image/jpeg",
                    size: 1024,
                    contentHash: Guid.NewGuid().ToString("N"));
                seeded.SetPoster("product-cover-poster.jpg");
                var media = await _mediaRepository.InsertAsync(seeded, autoSave: true);

                await _linkRepository.InsertAsync(
                    new EntityMediaLink(
                        companyId, MediaEntityNames.Product, withMedia, media.Id,
                        displayOrder: 0, isDefault: true, isActive: true),
                    autoSave: true);
                return media.Id;
            });

            var list = await _appService.GetListAsync(new ProductListRequestDto { MaxResultCount = 50 });

            var coveredRow = list.Items.Single(p => p.Id == withMedia);
            coveredRow.ImagePreviewUrl.ShouldNotBeNullOrEmpty();
            coveredRow.ImagePreviewUrl!.ShouldContain(mediaId.ToString());

            var bareRow = list.Items.Single(p => p.Id == withoutMedia);
            bareRow.ImagePreviewUrl.ShouldBeNull();
        }
    }

    private async Task<Guid> SeedProductAsync(Guid companyId, string code)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var product = await _productRepository.InsertAsync(
                new Product(companyId, code, $"Urun {code}"), autoSave: true);
            return product.Id;
        });
    }
}
