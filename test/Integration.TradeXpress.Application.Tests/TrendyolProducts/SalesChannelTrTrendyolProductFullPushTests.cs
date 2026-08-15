using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannelProducts;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolCategories;
using Integration.TradeXpress.Variants;
using CategoryValue = Integration.TradeXpress.TrendyolCategories.TrendyolAttributeValue;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// TRENDYOL TAM PUSH — uçtan uca uygulama ağı (bağımsız denetim bulgusu 2026-08-14: push'a bağlanan HİÇBİR
/// davranışın uygulama-seviyesi testi yoktu; sahte istemci create'i koşulsuz reddediyordu). Bu sınıf import →
/// doğrulama → görsel → push → batch finalize zincirini sahtelerle (ağ yok) koşturur ve şunları pinler:
/// ① kategori doğrulayıcısı gerçek push'ta koşar ve item-düzeyi eksen gövdeye girer (foto-öncelik);
/// ② SKU dondurma sonrası varianter İMZASI snapshot'a yazılır (3. aşama yeniden-bağlama ölü değil);
/// ③ defter satırı finalize'da PendingSent içeriğinden kurulur — başlık/eksen/görsel dolu, görsel listesi
///    FİİLEN gönderilen setten; ④ import RemoteImageUrls'ü damgayla yazar; ⑤ Trendyol-only kombinasyon:
///    fiyat+stok yoksa uyarıyla atlanır, yarım doluysa fail-fast, doluysa StockItem.Id ile SKU açılır.
/// </summary>
public abstract class SalesChannelTrTrendyolProductFullPushTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string ProductEntityName = "Product";

    private readonly ISalesChannelTrTrendyolProductAppService _appService;
    private readonly IProductAppService _productAppService;
    private readonly IMediaAppService _mediaService;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly FakeTrendyolProductClient _fakeClient;
    private readonly FakeTrendyolCategoryClient _fakeCategoryClient;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _channelProductRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductPushHistory, Guid> _historyRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItem, Guid> _headerRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly ICurrentCompany _currentCompany;

    protected SalesChannelTrTrendyolProductFullPushTests()
    {
        _appService = GetRequiredService<ISalesChannelTrTrendyolProductAppService>();
        _productAppService = GetRequiredService<IProductAppService>();
        _mediaService = GetRequiredService<IMediaAppService>();
        _entityMedia = GetRequiredService<IEntityMediaAppService>();
        _fakeClient = GetRequiredService<FakeTrendyolProductClient>();
        _fakeCategoryClient = GetRequiredService<FakeTrendyolCategoryClient>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrTrendyol, Guid>>();
        _channelProductRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProduct, Guid>>();
        _historyRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProductPushHistory, Guid>>();
        _headerRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProductStockItem, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _variantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    private const string TransparentPixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Full_push_validates_against_category_freezes_signature_and_writes_ledger_from_sent_content()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "PUSH1");
            SeedColorCategory("411");

            // İMPORT: iki kalem, Renk ekseni fotoğrafla gelir (kategori-tanımı eşleştirmesine GİRMEZ).
            _fakeClient.RemoteItems.Clear();
            _fakeClient.RemoteItems.Add(BuildRemoteItem("MAIN-P1", "BR-P1-RED", "STK-P1-RED", 686234, "Kırmızı", 5, 100m));
            _fakeClient.RemoteItems.Add(BuildRemoteItem("MAIN-P1", "BR-P1-BLUE", "STK-P1-BLUE", 686240, "Mavi", 3, 110m));
            await _appService.ImportFromMarketplaceAsync(channel.Id);

            var product = (await WithUnitOfWorkAsync(async () =>
                await _productRepository.GetListAsync(p => p.CompanyId == companyId))).ShouldHaveSingleItem();
            var record = (await WithUnitOfWorkAsync(async () =>
                await _channelProductRepository.GetListAsync(r => r.SalesChannelId == channel.Id))).ShouldHaveSingleItem();

            // ④ Import kanal adreslerini O ANKİ yerel setin damgasıyla yazdı (henüz DAM'da görsel yok → damga boş).
            record.RemoteImageUrls.Count.ShouldBe(1);
            record.RemoteImageMediaIds.ShouldBeEmpty();

            // Zorunlu ürün-seviyesi attribute (Materyal) doldur — doğrulayıcı kapısı için.
            await WithUnitOfWorkAsync(async () =>
            {
                var tracked = await _channelProductRepository.GetAsync(record.Id);
                // Import'un yazdığı ürün-seviyesi niteliklere (tek kalemli grupta Renk de oradadır) Materyal EKLENİR — ezilmez.
                tracked.SetAttributes(tracked.Attributes
                    .Where(a => a.AttributeId != 60)
                    .Append(new SalesChannelTrTrendyolProductCategoryAttribute(60, 1001, null))
                    .ToList());
                await _channelProductRepository.UpdateAsync(tracked, autoSave: true);
                return true;
            });

            // Satış-hazırlık kapısı + görsel.
            await _productAppService.VerifySaleReadinessAsync(new ProductSaleVerifyInputDto { ProductId = product.Id });
            var mediaId = await UploadAndLinkAsync(product.Id, "kapak.png");

            // PUSH (sahte create açık, batch makbuzu döner).
            _fakeClient.AllowProductSubmit = true;
            _fakeClient.NextBatchRequestId = "BATCH-CREATE-1";
            _fakeClient.SubmittedProducts.Clear();
            await _appService.PushToTrendyolAsync(record.Id);

            // ① Gövde: item-düzeyi eksen foto kimlikleriyle gitti, ürün-seviyesi Materyal kanonik listede.
            var sent = _fakeClient.SubmittedProducts.ShouldHaveSingleItem();
            sent.Items.Count.ShouldBe(2);
            sent.Items.Select(i => i.Attributes.Single().AttributeValueId).OrderBy(x => x)
                .ShouldBe(new int?[] { 686234, 686240 });
            sent.Attributes.ShouldHaveSingleItem().AttributeId.ShouldBe(60);
            sent.SentMediaIds.ShouldBe(new[] { mediaId });   // fiilen giden görsel seti gövdeyle birlikte taşınır

            // ② SKU dondurma: varianter imzası snapshot'a YAZILDI (yeniden-bağlama 3. aşaması artık eşleşebilir).
            var afterPush = await WithUnitOfWorkAsync(async () => await _channelProductRepository.GetAsync(record.Id));
            afterPush.Skus.Count.ShouldBe(2);
            afterPush.Skus.ShouldAllBe(s => s.AttributeSnapshot.Count == 1);
            afterPush.Skus.Single(s => s.Barcode == "BR-P1-RED").AttributeSnapshot.Single().AttributeValueId.ShouldBe(686234);

            // Bekleyen içerik submit anında dolu (başlık + eksen + görsel).
            var pendingRed = afterPush.Skus.Single(s => s.Barcode == "BR-P1-RED");
            pendingRed.PendingSentTitle.ShouldNotBeNullOrEmpty();
            pendingRed.PendingSentOptions.ShouldBe("Renk=Kırmızı");
            pendingRed.PendingSentMediaIds.ShouldBe(mediaId.ToString());

            // ARA DURUM: batch henüz işleniyor → hiçbir şey olmaz (bekleyen SİLİNMEZ, defter yazılmaz).
            _fakeClient.NextBatchStatus = new TrendyolBatchStatus("PROCESSING", 2, 0, null);
            await _appService.RefreshStatusAsync(record.Id);
            (await WithUnitOfWorkAsync(async () => await _historyRepository.GetListAsync(h => h.SalesChannelTrTrendyolProductId == record.Id)))
                .ShouldBeEmpty();
            (await WithUnitOfWorkAsync(async () => await _channelProductRepository.GetAsync(record.Id)))
                .Skus.Single(s => s.Barcode == "BR-P1-RED").PendingSentOptions.ShouldBe("Renk=Kırmızı");

            // TAMAMLANDI: ③ defter satırı PendingSent içeriğinden — Create türü, başlık/eksen/görsel DOLU.
            _fakeClient.NextBatchStatus = new TrendyolBatchStatus("COMPLETED", 2, 0, null);
            await _appService.RefreshStatusAsync(record.Id);

            var history = await WithUnitOfWorkAsync(async () =>
                await _historyRepository.GetListAsync(h => h.SalesChannelTrTrendyolProductId == record.Id));
            history.Count.ShouldBe(2);
            history.ShouldAllBe(h => h.PushKind == TrendyolProductPushKind.Create && h.Outcome == ChannelPushOutcome.Succeeded);
            var redRow = history.Single(h => h.Barcode == "BR-P1-RED");
            redRow.Title.ShouldNotBeNullOrEmpty();
            redRow.VariantOptions.ShouldBe("Renk=Kırmızı");
            redRow.Images.ShouldNotBeNull();
            redRow.Images!.ShouldStartWith(mediaId.ToString("N") + ":");   // "id:hash" biçimi, fiilen giden görsel

            // Terfi: LastSent* doldu, bekleyen temizlendi.
            var promoted = (await WithUnitOfWorkAsync(async () => await _channelProductRepository.GetAsync(record.Id)))
                .Skus.Single(s => s.Barcode == "BR-P1-RED");
            promoted.LastSentQuantity.ShouldBe(5);
            promoted.PendingSentOptions.ShouldBeNull();
            promoted.PendingSentMediaIds.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Missing_mandatory_product_attribute_stops_the_real_push_at_the_gate()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "PUSH2");
            SeedColorCategory("411");
            _fakeClient.RemoteItems.Clear();
            // Materyal (Required, varianter değil) importta HİÇ gelmez → ürün seviyesinde de yok.
            _fakeClient.RemoteItems.Add(BuildRemoteItem("MAIN-P2", "BR-P2-RED", "STK-P2-RED", 686234, "Kırmızı", 5, 100m, withMaterial: false));
            await _appService.ImportFromMarketplaceAsync(channel.Id);

            var product = (await WithUnitOfWorkAsync(async () =>
                await _productRepository.GetListAsync(p => p.CompanyId == companyId))).ShouldHaveSingleItem();
            var record = (await WithUnitOfWorkAsync(async () =>
                await _channelProductRepository.GetListAsync(r => r.SalesChannelId == channel.Id))).ShouldHaveSingleItem();
            await _productAppService.VerifySaleReadinessAsync(new ProductSaleVerifyInputDto { ProductId = product.Id });
            await UploadAndLinkAsync(product.Id, "kapak2.png");

            _fakeClient.AllowProductSubmit = true;
            _fakeClient.SubmittedProducts.Clear();

            // Materyal (Required, varianter değil) DOLDURULMADI → kapıda durur, kanala hiçbir şey gitmez.
            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.PushToTrendyolAsync(record.Id));
            ex.Code.ShouldBe("TradeXpress:Trendyol:Product:ProductAttributeMissing");
            _fakeClient.SubmittedProducts.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Trendyol_only_combination_without_price_and_stock_is_skipped_but_half_filled_one_stops_the_push()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "PUSH3");
            SeedColorCategory("411");
            _fakeClient.RemoteItems.Clear();
            _fakeClient.RemoteItems.Add(BuildRemoteItem("MAIN-P3", "BR-P3-RED", "STK-P3-RED", 686234, "Kırmızı", 5, 100m));
            await _appService.ImportFromMarketplaceAsync(channel.Id);

            var product = (await WithUnitOfWorkAsync(async () =>
                await _productRepository.GetListAsync(p => p.CompanyId == companyId))).ShouldHaveSingleItem();
            var record = (await WithUnitOfWorkAsync(async () =>
                await _channelProductRepository.GetListAsync(r => r.SalesChannelId == channel.Id))).ShouldHaveSingleItem();
            await WithUnitOfWorkAsync(async () =>
            {
                var tracked = await _channelProductRepository.GetAsync(record.Id);
                // Import'un yazdığı ürün-seviyesi niteliklere (tek kalemli grupta Renk de oradadır) Materyal EKLENİR — ezilmez.
                tracked.SetAttributes(tracked.Attributes
                    .Where(a => a.AttributeId != 60)
                    .Append(new SalesChannelTrTrendyolProductCategoryAttribute(60, 1001, null))
                    .ToList());
                await _channelProductRepository.UpdateAsync(tracked, autoSave: true);
                return true;
            });
            await _productAppService.VerifySaleReadinessAsync(new ProductSaleVerifyInputDto { ProductId = product.Id });
            await UploadAndLinkAsync(product.Id, "kapak3.png");

            // Trendyol-only kombinasyon başlığı (ERP karşılığı yok) — kanal özellik grafında GERÇEK "Renk=Mavi"
            // (imza kanal Guid'leriyle; ad→id türetimi kategori tanımına karşı çözülür) — override'sız: NE fiyat NE stok.
            var attributeRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProductAttribute, Guid>>();
            var valueRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProductAttributeValue, Guid>>();
            var emptyHeader = await WithUnitOfWorkAsync(async () =>
            {
                var attribute = await attributeRepository.InsertAsync(
                    new SalesChannelTrTrendyolProductAttribute(companyId, record.Id, "Renk", 0), autoSave: true);
                var value = await valueRepository.InsertAsync(
                    new SalesChannelTrTrendyolProductAttributeValue(companyId, attribute.Id, "Mavi", 0), autoSave: true);
                var h = new SalesChannelTrTrendyolProductStockItem(companyId, record.Id, productVariantId: null);
                h.SetCombinationSignature($"{attribute.Id}={value.Id}");
                return await _headerRepository.InsertAsync(h, autoSave: true);
            });

            _fakeClient.AllowProductSubmit = true;
            _fakeClient.SubmittedProducts.Clear();

            // Boş kombinasyon ATLANIR — ERP satırı yine gider (senkron ölmez).
            var dto = await _appService.PushToTrendyolAsync(record.Id);
            _fakeClient.SubmittedProducts.ShouldHaveSingleItem().Items.Count.ShouldBe(1);
            dto.SyncWarnings.ShouldContain(w => w.Contains("gönderime alınmadı", StringComparison.OrdinalIgnoreCase)
                                             || w.Contains("left out", StringComparison.OrdinalIgnoreCase));

            // YARIM dolu (fiyat var, stok yok) → belirsizlik, fail-fast (sessiz geçilmez).
            await WithUnitOfWorkAsync(async () =>
            {
                var h = await _headerRepository.GetAsync(emptyHeader.Id);
                h.SetOverridePrice(150m, null);
                await _headerRepository.UpdateAsync(h, autoSave: true);
                return true;
            });
            _fakeClient.SubmittedProducts.Clear();
            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.PushToTrendyolAsync(record.Id));
            ex.Code.ShouldBe("TradeXpress:Trendyol:Product:PriceMissingForPush");
            _fakeClient.SubmittedProducts.ShouldBeEmpty();
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    private void SeedColorCategory(string categoryId)
    {
        _fakeCategoryClient.LeafAttributes[categoryId] = new TrendyolLeafAttributes(categoryId, new[]
        {
            new TrendyolAttributeDef(47, "Renk", Required: true, Varianter: true, AllowCustom: false, new[]
            {
                new CategoryValue(686234, "Kırmızı"),
                new CategoryValue(686240, "Mavi"),
            }),
            new TrendyolAttributeDef(60, "Materyal", Required: true, Varianter: false, AllowCustom: false, new[]
            {
                new CategoryValue(1001, "Deri"),
            }),
        });
    }

    private async Task<Guid> UploadAndLinkAsync(Guid productId, string fileName)
    {
        var media = await WithUnitOfWorkAsync(async () => await _mediaService.UploadAsync(new MediaUploadDto
        {
            FileName = fileName,
            Content = Convert.FromBase64String(TransparentPixelPng),
        }));
        await WithUnitOfWorkAsync(async () =>
        {
            await _entityMedia.ReplaceForAsync(MediaEntityNames.Product, productId, null, new List<EntityMediaLinkEditDto>
            {
                new() { MediaId = media.Id, IsActive = true, IsDefault = true, DisplayOrder = 0 },
            });
            return true;
        });
        return media.Id;
    }

    private async Task<SalesChannelTrTrendyol> SeedChannelAsync(Guid companyId, string suffix)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _channelRepository.InsertAsync(
                new SalesChannelTrTrendyol(companyId, $"TY-{suffix}", $"Trendyol Kanal {suffix}", "seller-1", "api-key", "api-secret"),
                autoSave: true));
    }

    /// <summary>Renk ekseni fotoğraflı düz uzak kalem (item-başı Attributes: Renk=<paramref name="colorValueId"/>).
    /// <paramref name="withMaterial"/>=false: Materyal(60) niteliği HİÇ gelmez — zorunlu-attribute-eksik senaryosu.</summary>
    private static TrendyolRemoteProduct BuildRemoteItem(
        string mainId, string barcode, string stockCode, int colorValueId, string colorText, int quantity, decimal price,
        bool withMaterial = true)
    {
        var attributes = new List<TrendyolRemoteAttribute> { new(47, "Renk", colorValueId, colorText, null) };
        if (withMaterial)
        {
            attributes.Add(new TrendyolRemoteAttribute(60, "Materyal", 1001, "Deri", null));
        }

        return new TrendyolRemoteProduct(
            ProductMainId: mainId,
            Title: "Deri Kılıf",
            Description: "İçe aktarma testi için yeterince uzun açıklama metni.",
            CategoryId: "411",
            CategoryName: "Telefon Kılıfı",
            BrandId: "82",
            BrandName: "MarkaX",
            VatRate: 20,
            DimensionalWeight: 0.5m,
            DeliveryDuration: 2,
            ImageUrls: new List<string> { "https://cdn.example.com/img-" + barcode + ".jpg" },
            Variants: new List<TrendyolRemoteVariant>
            {
                new(
                    Barcode: barcode,
                    StockCode: stockCode,
                    Quantity: quantity,
                    ListPrice: price,
                    SalePrice: price,
                    ProductContentId: 5000 + barcode.Length,
                    Approved: true,
                    OnSale: true,
                    Attributes: attributes),
            });
    }
}
