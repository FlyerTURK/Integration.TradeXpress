using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 push GÖRSEL sözleşmesi — GOLDEN testler.
///
/// <para><b>Neden yazıldı:</b> görsel kaynağı legacy <c>ProductImage</c>'dan merkezi DAM'a taşınacak
/// (K2 kararı). Bugünkü push davranışının HİÇBİR testi yoktu: mevcut push testleri yalnız
/// <c>ImagesRequired</c> guard'ını geçmek için fixture kuruyor, sıra/kapak/numaralandırma assert etmiyordu.
/// Kaynak değiştiğinde sessizce bozulabilecek dört kural burada kilitlenir.</para>
///
/// <para><b>Göç sonrası:</b> bu testler AYNEN geçmeli — yalnız fixture DAM'a kurulacak şekilde değişir.
/// Kırmızı yanarlarsa pazaryerinde kapak görseli değişmiş ya da sıra bozulmuş demektir.</para>
/// </summary>
public abstract class SalesChannelTrN11ProductImagePushTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ISalesChannelTrN11ProductAppService _appService;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantDetail, Guid> _variantDetailRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly FakeN11ProductClient _fakeClient;

    protected SalesChannelTrN11ProductImagePushTests()
    {
        _appService = GetRequiredService<ISalesChannelTrN11ProductAppService>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _variantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _variantDetailRepository = GetRequiredService<IRepository<ProductVariantDetail, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
        _fakeClient = GetRequiredService<FakeN11ProductClient>();
    }

    [Fact]
    public async Task Cover_image_is_pushed_first_regardless_of_display_order()
    {
        // KURAL 1: kapak (IsDefault) HER ZAMAN ilk sırada gider — DisplayOrder'ı büyük olsa bile.
        // DAM'da IsDefault ile DisplayOrder bağımsızdır (3. sıradaki medya kapak olabilir), bu yüzden
        // sıralama push tarafında AÇIKÇA uygulanmalı; yoksa pazaryerinde kapak görsel değişir.
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "IMGORDER", new[]
            {
                ("https://example.com/a.jpg", 0, false),
                ("https://example.com/b.jpg", 1, false),
                ("https://example.com/cover.jpg", 2, true),   // kapak EN SONDA tanımlı
            });

            await _appService.PushToN11Async(created.Id);

            var images = _fakeClient.LastSavedProduct.ShouldNotBeNull().Images;
            images[0].Url.ShouldBe("https://example.com/cover.jpg");
            images.Select(i => i.Url).ShouldBe(new[]
            {
                "https://example.com/cover.jpg",
                "https://example.com/a.jpg",
                "https://example.com/b.jpg",
            });
        }
    }

    [Fact]
    public async Task Image_order_is_one_based_and_gapless()
    {
        // KURAL 2: N11 'order' alanı 1'den başlar ve BOŞLUKSUZ artar. Göçte pasif/video medya
        // filtrelendikten SONRA yeniden numaralandırılmalı — aksi halde dizide delik kalır.
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "IMGSEQ", new[]
            {
                ("https://example.com/1.jpg", 0, true),
                ("https://example.com/2.jpg", 1, false),
                ("https://example.com/3.jpg", 2, false),
            });

            await _appService.PushToN11Async(created.Id);

            var images = _fakeClient.LastSavedProduct.ShouldNotBeNull().Images;
            images.Select(i => i.Order).ShouldBe(new[] { 1, 2, 3 });
        }
    }

    [Fact]
    public async Task Push_fails_when_no_usable_image_exists()
    {
        // KURAL 3: kullanılabilir tek görsel yoksa push BAŞLAMAZ (fail-fast). Görselsiz listeleme
        // pazaryerinde reddedilir; hatayı burada söylemek, uzak tarafta almaktan iyidir.
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "IMGNONE", Array.Empty<(string, int, bool)>());

            var exception = await Should.ThrowAsync<BusinessException>(() => _appService.PushToN11Async(created.Id));

            exception.Code.ShouldBe("TradeXpress:N11:Product:ImagesRequired");
        }
    }

    [Fact]
    public async Task At_most_the_allowed_number_of_images_is_pushed()
    {
        // KURAL 4: N11 ürün başına en fazla ProductConsts.MaxImageCount görsel kabul ediyor. Bugün sınır
        // KAYNAKTA uygulanıyor (Product.SetImages kırpıyor); DAM'da link sayısı sınırsız olduğundan
        // göçten sonra sınırın PUSH tarafında uygulanması gerekecek. Test her iki durumda da geçerli.
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var many = Enumerable.Range(0, ProductConsts.MaxImageCount + 4)
                .Select(i => ($"https://example.com/{i}.jpg", i, i == 0))
                .ToArray();

            var created = await SeedAsync(companyId, "IMGMAX", many);

            await _appService.PushToN11Async(created.Id);

            var images = _fakeClient.LastSavedProduct.ShouldNotBeNull().Images;
            images.Count.ShouldBeLessThanOrEqualTo(ProductConsts.MaxImageCount);
            images.Select(i => i.Order).ShouldBe(Enumerable.Range(1, images.Count));
        }
    }

    /// <summary>Kanal + ürün + kanal ürünü kurar. Görseller (url, displayOrder, isDefault) üçlüsüyle verilir;
    /// hepsi URL kaynaklıdır (blob dış-link sağlayıcısı testte yapılandırılmamıştır).</summary>
    private async Task<SalesChannelTrN11ProductDto> SeedAsync(
        Guid companyId, string productCode, IReadOnlyList<(string Url, int Order, bool IsDefault)> images)
    {
        var (channel, product) = await WithUnitOfWorkAsync(async () =>
        {
            var ch = await _channelRepository.InsertAsync(
                new SalesChannelTrN11(companyId, $"N11-{productCode}", $"N11 Kanal {productCode}", "app-key", "app-secret"),
                autoSave: true);

            var p = new Product(companyId, productCode, $"Urun {productCode}");
            if (images.Count > 0)
            {
                p.SetImages(images.Select(i =>
                    new ProductImage(ProductImageSourceType.Url, i.Url, null, null, i.Order, i.IsDefault, null, null)));
            }

            await _productRepository.InsertAsync(p, autoSave: true);

            // Push en az bir FİYATLI varyant ister (NoPricedVariant guard'ı) — tek ana varyant yeter.
            var mainVariant = await _variantRepository.InsertAsync(
                new EntityVariant(companyId, "Product", p.Id, ProductConsts.MainVariantCode,
                    ProductConsts.MainVariantName, isMain: true, isActive: true),
                autoSave: true);
            var detail = new ProductVariantDetail(companyId, mainVariant.Id);
            detail.SetSalePrice(100m, null);
            await _variantDetailRepository.InsertAsync(detail, autoSave: true);

            return (ch, p);
        });

        return await _appService.CreateAsync(new SalesChannelTrN11ProductCreateDto
        {
            ProductId = product.Id,
            SalesChannelId = channel.Id,
            CategoryExternalId = FakeN11CategoryClient.DefaultCategoryExternalId,
            ShipmentTemplateName = "Standart Teslimat",
        });
    }
}
