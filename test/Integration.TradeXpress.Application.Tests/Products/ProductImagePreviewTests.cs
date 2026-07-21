using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// GEÇİCİ doğrulama testi (2026-07-09) — Product grid thumbnail özelliği: <c>ProductAppService.GetListAsync</c>
/// materyalize edilmiş <c>Product.Images</c> (owned JSON kolonu) üzerinden VARSAYILAN görseli C# tarafında seçip
/// (Url → direkt link, Upload → thumbnail blob'undan data-URL) DTO'ya yazıyor mu — gerçek Sqlite DB ile runtime
/// doğrulaması (EF JSON owned-collection materyalizasyonu + blob okuma, tahmin değil kanıt).
/// </summary>
public abstract class ProductImagePreviewTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    // 1x1 şeffaf PNG — ImageSharp thumbnail üretimi gerçek bir görsel ister.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly IProductAppService _productAppService;
    private readonly IProductImageAppService _imageAppService;
    private readonly ICurrentCompany _currentCompany;

    protected ProductImagePreviewTests()
    {
        _productAppService = GetRequiredService<IProductAppService>();
        _imageAppService = GetRequiredService<IProductImageAppService>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task GetList_fills_upload_and_url_preview_and_leaves_imageless_null()
    {
        using (_currentCompany.Change(Guid.NewGuid()))
        {
            var uploadResult = await _imageAppService.UploadAsync(new ProductImageUploadDto
            {
                FileName = "test.png",
                Content = TinyPng,
                ProductCode = "IMGUP",
            });

            var uploadProduct = await _productAppService.CreateAsync(new ProductCreateDto
            {
                Code = "IMGUP",
                Name = "Upload Görselli Ürün",
                Images = new List<ProductImageGraphDto>
                {
                    new()
                    {
                        SourceType = ProductImageSourceType.Upload,
                        BlobName = uploadResult.BlobName,
                        FileName = "test.png",
                        DisplayOrder = 0,
                        IsDefault = true,
                    },
                },
            });

            var urlProduct = await _productAppService.CreateAsync(new ProductCreateDto
            {
                Code = "IMGURL",
                Name = "URL Görselli Ürün",
                Images = new List<ProductImageGraphDto>
                {
                    new()
                    {
                        SourceType = ProductImageSourceType.Url,
                        Url = "https://example.com/pic.jpg",
                        DisplayOrder = 0,
                        IsDefault = true,
                    },
                },
            });

            var bareProduct = await _productAppService.CreateAsync(new ProductCreateDto
            {
                Code = "IMGNONE",
                Name = "Görselsiz Ürün",
            });

            var list = await _productAppService.GetListAsync(new ProductListRequestDto { MaxResultCount = 100 });

            var upDto = list.Items.Single(x => x.Id == uploadProduct.Id);
            upDto.ImagePreviewUrl.ShouldNotBeNullOrEmpty();
            upDto.ImagePreviewUrl.ShouldBe(uploadResult.PreviewDataUrl);   // aynı thumbnail'den üretilmiş data-URL

            var urlDto = list.Items.Single(x => x.Id == urlProduct.Id);
            urlDto.ImagePreviewUrl.ShouldBe("https://example.com/pic.jpg");

            var bareDto = list.Items.Single(x => x.Id == bareProduct.Id);
            bareDto.ImagePreviewUrl.ShouldBeNull();
        }
    }
}
