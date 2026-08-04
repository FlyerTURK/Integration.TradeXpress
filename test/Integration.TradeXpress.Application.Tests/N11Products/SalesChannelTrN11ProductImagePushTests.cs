using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
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
    private readonly IRepository<Media, Guid> _mediaRepository;
    private readonly IRepository<EntityMediaLink, Guid> _linkRepository;
    private readonly ICurrentCompany _currentCompany;
    // Push artik REST ten gidiyor (SOAP urun uclari N11 tarafinda kapatildi) → iddialar product-create
    // satirlari uzerinde. Yapisal fark: SOAP tek urun + icinde stockItems, REST her SKU icin AYRI satir.
    private readonly FakeN11ProductRestClient _restClient;

    /// <summary>Seed'lenen medyanın dosya adı → Id eşlemesi. Push adresleri artık İMZALI olduğundan (içerik
    /// tahmin edilemez) sıra assert'leri medya kimliği üzerinden yapılır; adresin içinde Id düz metin geçer.</summary>
    private readonly Dictionary<string, Guid> _seededMediaIds = new();

    /// <summary>SeedAsync'in kurduğu ANA varyantın Id'si — varyant-bağlamı ("ProductVariant") medya fixture'larının çapası.</summary>
    private Guid _seededMainVariantId;

    protected SalesChannelTrN11ProductImagePushTests()
    {
        _appService = GetRequiredService<ISalesChannelTrN11ProductAppService>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _variantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _variantDetailRepository = GetRequiredService<IRepository<ProductVariantDetail, Guid>>();
        _mediaRepository = GetRequiredService<IRepository<Media, Guid>>();
        _linkRepository = GetRequiredService<IRepository<EntityMediaLink, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
        _restClient = GetRequiredService<FakeN11ProductRestClient>();
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
                ("a.jpg", 0, false),
                ("b.jpg", 1, false),
                ("cover.jpg", 2, true),   // kapak EN SONDA tanımlı
            });

            await _appService.PushToN11Async(created.Id);

            var rows = _restClient.LastCreatedRows;
            rows.ShouldNotBeEmpty();
            var images = rows[0].Images;
            images.Count.ShouldBe(3);
            images[0].Url.ShouldContain(MediaTokenOf("cover.jpg"));
            images[1].Url.ShouldContain(MediaTokenOf("a.jpg"));
            images[2].Url.ShouldContain(MediaTokenOf("b.jpg"));
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
                ("1.jpg", 0, true),
                ("2.jpg", 1, false),
                ("3.jpg", 2, false),
            });

            await _appService.PushToN11Async(created.Id);

            var rows = _restClient.LastCreatedRows;
            rows.ShouldNotBeEmpty();
            var images = rows[0].Images;
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
                .Select(i => ($"{i}.jpg", i, i == 0))
                .ToArray();

            var created = await SeedAsync(companyId, "IMGMAX", many);

            await _appService.PushToN11Async(created.Id);

            var rows = _restClient.LastCreatedRows;
            rows.ShouldNotBeEmpty();
            var images = rows[0].Images;
            images.Count.ShouldBeLessThanOrEqualTo(ProductConsts.MaxImageCount);
            images.Select(i => i.Order).ShouldBe(Enumerable.Range(1, images.Count));
        }
    }

    [Fact]
    public async Task Variant_only_images_still_push_via_fallback()
    {
        // KURAL 5: ürün-düzeyi push VARYANT setlerini de kapsar (MarketplacePushImageResolver.AppendVariantMediaAsync —
        // 2026-08-01 Hakan kararı: varyant görselini ayrıca taşıyamayan kanalda varyant fotoğrafları ana ürün
        // fotoğraflarına EKLENİR; aktif varyantlar ana-önce, MediaId dedup). Fotoğrafları YALNIZ varyant panelinden
        // ekleyen ürün pazaryerine çıkabilmeli; birleştirme olmasaydı görseller DAM'da dururken push ImagesRequired ile düşerdi.
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            // Ürün bağlamına HİÇ medya kurulmaz (boş görsel listesi) — tek görsel ana varyantın KENDİ bağlamında.
            var created = await SeedAsync(companyId, "IMGVARFB", Array.Empty<(string, int, bool)>());
            await SeedVariantMediaAsync(companyId, _seededMainVariantId, "variant.jpg");

            await _appService.PushToN11Async(created.Id);

            var rows = _restClient.LastCreatedRows;
            rows.ShouldNotBeEmpty();
            var images = rows[0].Images;
            images.Count.ShouldBe(1);
            images[0].Url.ShouldContain(MediaTokenOf("variant.jpg"));
            images[0].Order.ShouldBe(1);
        }
    }

    /// <summary>Seed'lenen medyanın imzalı adreste düz metin geçen kimliği — sıra assert'lerinin çapası.</summary>
    private string MediaTokenOf(string fileName)
    {
        return _seededMediaIds[fileName].ToString("N");
    }

    /// <summary>Kanal + ürün + kanal ürünü kurar. Görseller (ad, displayOrder, isDefault) üçlüsüyle verilir ve
    /// merkezi DAM'a kurulur: kütüphane kaydı (<see cref="Media"/>) + ürün bağlamına link. Push'un TEK görsel
    /// kaynağı budur; dış adres imzalı sağlayıcıdan üretilir (anahtar TestBase appsettings'inde).</summary>
    private async Task<SalesChannelTrN11ProductDto> SeedAsync(
        Guid companyId, string productCode, IReadOnlyList<(string Name, int Order, bool IsDefault)> images)
    {
        var (channel, product) = await WithUnitOfWorkAsync(async () =>
        {
            var ch = await _channelRepository.InsertAsync(
                new SalesChannelTrN11(companyId, $"N11-{productCode}", $"N11 Kanal {productCode}", "app-key", "app-secret"),
                autoSave: true);

            var p = new Product(companyId, productCode, $"Urun {productCode}");
            await _productRepository.InsertAsync(p, autoSave: true);

            foreach (var image in images)
            {
                var media = await _mediaRepository.InsertAsync(
                    new Media(
                        companyId,
                        MediaType.Image,
                        blobName: Guid.NewGuid().ToString("N"),
                        fileName: image.Name,
                        contentType: "image/jpeg",
                        size: 1024,
                        contentHash: Guid.NewGuid().ToString("N")),
                    autoSave: true);

                await _linkRepository.InsertAsync(
                    new EntityMediaLink(
                        companyId, MediaEntityNames.Product, p.Id, media.Id, image.Order, image.IsDefault, isActive: true),
                    autoSave: true);

                _seededMediaIds[image.Name] = media.Id;
            }

            // Push en az bir FİYATLI varyant ister (NoPricedVariant guard'ı) — tek ana varyant yeter.
            var mainVariant = await _variantRepository.InsertAsync(
                new EntityVariant(companyId, "Product", p.Id, ProductConsts.MainVariantCode,
                    ProductConsts.MainVariantName, isMain: true, isActive: true),
                autoSave: true);
            _seededMainVariantId = mainVariant.Id;
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
            // REST push KDV oranını ZORUNLU kılıyor (create'te "Evet"); boşsa mapper fail-fast eder.
            VatRate = 20,
            ShipmentTemplateName = "Standart Teslimat",
        });
    }

    /// <summary>Bir varyantın KENDİ bağlamına ("ProductVariant" + varyant Id) tek görsel bağlar — ürün-geneli set
    /// boşken devreye giren varyant geri-düşüşü senaryolarının fixture'ı. Medya kimliği <see cref="MediaTokenOf"/>
    /// çapasına kaydedilir.</summary>
    private async Task SeedVariantMediaAsync(Guid companyId, Guid variantId, string fileName)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var media = await _mediaRepository.InsertAsync(
                new Media(
                    companyId,
                    MediaType.Image,
                    blobName: Guid.NewGuid().ToString("N"),
                    fileName: fileName,
                    contentType: "image/jpeg",
                    size: 1024,
                    contentHash: Guid.NewGuid().ToString("N")),
                autoSave: true);

            await _linkRepository.InsertAsync(
                new EntityMediaLink(
                    companyId, MediaEntityNames.ProductVariant, variantId, media.Id,
                    displayOrder: 0, isDefault: true, isActive: true),
                autoSave: true);

            _seededMediaIds[fileName] = media.Id;
        });
    }
}
