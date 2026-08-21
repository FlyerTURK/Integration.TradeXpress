using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannelProducts;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// <see cref="IProductAppService.GetSaleReadinessAsync"/> uçtan uca (EF): snapshot GERÇEK tablolardan yüklenir,
/// yargı <see cref="ProductSaleValidator"/>'dan, kanal satırı Can* bayraklarıyla birlikte gelir. Saf validator
/// testleri kuralı çiviler; bu sınıf "builder doğru veriyi doğru yere taşıyor mu" sorusunu sorar — özellikle
/// satılabilirliğin GUARD'DAN (<c>VariantSaleReadinessResolver</c>) okunduğunu, rozetten değil.
/// </summary>
public abstract class ProductSaleReadinessTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string ProductEntityName = "Product";

    private readonly IProductAppService _productAppService;
    private readonly IRepository<Product, Guid> _products;
    private readonly IRepository<EntityVariant, Guid> _variants;
    private readonly IRepository<ProductVariantDetail, Guid> _details;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLines;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _trendyolChannels;
    private readonly IRepository<SalesChannelTrN11, Guid> _n11Channels;
    private readonly IRepository<SalesChannelTrN11Product, Guid> _n11Products;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _trendyolProducts;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly IMediaAppService _media;
    private readonly ICurrentCompany _currentCompany;

    protected ProductSaleReadinessTests()
    {
        _productAppService = GetRequiredService<IProductAppService>();
        _products = GetRequiredService<IRepository<Product, Guid>>();
        _variants = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _details = GetRequiredService<IRepository<ProductVariantDetail, Guid>>();
        _recipeLines = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
        _trendyolChannels = GetRequiredService<IRepository<SalesChannelTrTrendyol, Guid>>();
        _n11Channels = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _n11Products = GetRequiredService<IRepository<SalesChannelTrN11Product, Guid>>();
        _trendyolProducts = GetRequiredService<IRepository<SalesChannelTrTrendyolProduct, Guid>>();
        _entityMedia = GetRequiredService<IEntityMediaAppService>();
        _media = GetRequiredService<IMediaAppService>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    /// <summary>① Fiyatlı + fiyatsız iki varyant, kategorili Fixed ürün, görselsiz: sayaçlar ve issue kodları
    /// gerçek tablolardan doğru çıkar; doğrulama yapılmadığı için guard kapalı (SellableVariantCount = 0) ve
    /// CanVerify yine true (fiyatlı kardeş var).</summary>
    [Fact]
    public async Task Readiness_reflects_variants_prices_and_gate_from_real_tables()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var scenario = await SeedProductAsync(companyId, "RDY1", priceSecondVariant: false);

            var readiness = await _productAppService.GetSaleReadinessAsync(scenario.ProductId);

            readiness.ProductId.ShouldBe(scenario.ProductId);
            readiness.HasCategory.ShouldBeTrue();
            readiness.StockPolicy.ShouldBe(ProductStockPolicy.Fixed);
            readiness.ActiveVariantCount.ShouldBe(2);
            readiness.PricedVariantCount.ShouldBe(1);
            readiness.RecipeVariantCount.ShouldBe(2);
            readiness.SellableVariantCount.ShouldBe(0);   // guard kapalı: kimse doğrulamadı
            readiness.DraftVariantCount.ShouldBe(2);
            readiness.ImageCount.ShouldBe(0);
            readiness.CanVerify.ShouldBeTrue();

            readiness.Issues.ShouldContain(i => i.Code == ProductSaleValidator.VariantNoSalePrice
                                               && i.TargetId == scenario.VariantIds[1]);
            readiness.Issues.ShouldContain(i => i.Code == ProductSaleValidator.ProductNoImage);
            readiness.Issues.ShouldContain(i => i.Code == ProductSaleValidator.ChannelNone);
            readiness.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ProductNoCategory);

            readiness.Steps.Select(s => s.Key).ShouldBe(new[]
            {
                ProductSaleValidator.StepCategory, ProductSaleValidator.StepVariants, ProductSaleValidator.StepRecipe,
                ProductSaleValidator.StepImages, ProductSaleValidator.StepVerification,
                ProductSaleValidator.StepChannelProducts, ProductSaleValidator.StepPush,
            });
            readiness.Steps.Single(s => s.Key == ProductSaleValidator.StepVariants).State.ShouldBe(SaleReadinessStepState.Blocked);
            readiness.Channels.ShouldBeEmpty();
        }
    }

    /// <summary>② Doğrulama SONRASI guard açılır ve satışa hazırlık paneli bunu GUARD'DAN okur; kanal satırları
    /// Can* bayraklarıyla gelir. Ürün GÖRSELSİZ olduğundan iki kanalda da CanPush KAPALIDIR (push ImagesRequired
    /// ile düşerdi); N11'de senkron aktif + SKU'lu kayıtta açık kalır (senkron görsel istemez).</summary>
    [Fact]
    public async Task Readiness_reads_sellability_from_the_gate_and_builds_channel_rows()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var scenario = await SeedProductAsync(companyId, "RDY2", priceSecondVariant: true);

            var channel = await WithUnitOfWorkAsync(() => _trendyolChannels.InsertAsync(
                new SalesChannelTrTrendyol(companyId, "TY-RDY2", "Trendyol RDY2", "seller-1", "api-key", "api-secret"),
                autoSave: true));
            var channelProduct = await WithUnitOfWorkAsync(() => _trendyolProducts.InsertAsync(
                new SalesChannelTrTrendyolProduct(companyId, channel.Id, scenario.ProductId, "MAIN-RDY2", 1, "cat-1", "brand-1"),
                autoSave: true));

            // N11 kolunun AĞI (2026-08-21 hakem bulgusu): CanPush görsel şartı ve CanSyncStockPrice'ın
            // IsActive konjonktu yalnız Trendyol satırında pinliydi — N11 satırındaki aynı iki karar test
            // ağının tamamen dışındaydı, sabotajda hiçbir test kırmızı yanmıyordu. Aktif + SKU'lu N11 kaydı:
            // görselsiz üründe CanPush KAPALI, senkron AÇIK (senkron görsel istemez).
            var n11Channel = await WithUnitOfWorkAsync(() => _n11Channels.InsertAsync(
                new SalesChannelTrN11(companyId, "N11-RDY2", "N11 RDY2", "app-key", "app-secret"),
                autoSave: true));
            await WithUnitOfWorkAsync(async () =>
            {
                var n11 = new SalesChannelTrN11Product(
                    companyId, n11Channel.Id, scenario.ProductId, "MAIN-RDY2", 1, "cat-1", "Sablon");
                n11.Skus.Add(new SalesChannelTrN11ProductSku(scenario.VariantIds[0], "MAIN-RDY2-1"));
                await _n11Products.InsertAsync(n11, autoSave: true);
            });

            var before = await _productAppService.GetSaleReadinessAsync(scenario.ProductId);
            before.SellableVariantCount.ShouldBe(0);

            var verify = await _productAppService.VerifySaleReadinessAsync(
                new ProductSaleVerifyInputDto { ProductId = scenario.ProductId });
            verify.VerifiedVariants.ShouldBe(2);

            var after = await _productAppService.GetSaleReadinessAsync(scenario.ProductId);
            after.SellableVariantCount.ShouldBe(2);
            after.StaleVerifiedVariantCount.ShouldBe(0);
            after.DraftVariantCount.ShouldBe(0);
            after.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.VariantNotVerified);
            after.Steps.Single(s => s.Key == ProductSaleValidator.StepVerification).State.ShouldBe(SaleReadinessStepState.Done);

            after.Channels.Count.ShouldBe(2);

            var n11Row = after.Channels.Single(r => r.ChannelType == SalesChannelType.TrN11);
            n11Row.CanPush.ShouldBeFalse("Görselsiz üründe N11 push'u da ImagesRequired ile düşer — düğme kapalı.");
            n11Row.CanSyncStockPrice.ShouldBeTrue("Senkron görsel istemez; aktif + SKU'lu kayıtta açık kalır.");

            var row = after.Channels.Single(r => r.ChannelType == SalesChannelType.TrTrendyol);
            row.ChannelType.ShouldBe(SalesChannelType.TrTrendyol);
            row.ChannelProductId.ShouldBe(channelProduct.Id);
            row.SalesChannelCode.ShouldBe("TY-RDY2");
            row.IsListed.ShouldBeFalse();
            row.IsPending.ShouldBeFalse();
            row.Readiness.ShouldBe(ChannelProductReadiness.Ready);

            // 2026-08-21 kural değişikliği: "Gönder" GÖRSELSİZ üründe KAPALI. Bu senaryonun ürününde hiç görsel
            // yok, dolayısıyla gerçek push zaten fail-fast ederdi (Trendyol/N11: ImagesRequired). Düğmeyi açık
            // bırakmak kullanıcıyı kaçınılmaz bir hataya davet ediyordu; ölçümde bu "panel sessiz, push patlıyor"
            // sınıfının en sık yaşanan örneğiydi. Görselli karşılığı: Push_is_offered_when_the_only_images_live_on_a_variant.
            row.CanPush.ShouldBeFalse();
            row.CanSyncStockPrice.ShouldBeFalse();
            row.CanRefreshStatus.ShouldBeFalse();
            row.CanResolveQueue.ShouldBeFalse();
            row.CanToggleArchive.ShouldBeFalse();
            row.LastPushedAt.ShouldBeNull();

            after.Issues.ShouldContain(i => i.Code == ProductSaleValidator.ChannelNotPushed
                                            && i.TargetId == channelProduct.Id
                                            && i.ChannelType == SalesChannelType.TrTrendyol);
        }
    }

    /// <summary>③ REÇETE ŞABLONU uyarısı GERÇEK ürün satırından okunur: şablonsuz kayıtta issue doğar, kolona
    /// bir şablon yazılınca kaybolur. İki kayıt da sorunsuz eklenir/güncellenir — zorunluluk 2026-08-20 Hakan
    /// kararıyla veritabanında değil SATIŞA HAZIRLIK PANELİNDE yaşar; bu fact tam olarak o ayrımı çiviler (uyarı var ama
    /// <c>CanVerify</c> açık kalıyor).</summary>
    [Fact]
    public async Task Missing_recipe_template_is_reported_from_the_real_product_row()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var scenario = await SeedProductAsync(companyId, "RDY3", priceSecondVariant: true);

            var before = await _productAppService.GetSaleReadinessAsync(scenario.ProductId);

            var issue = before.Issues.Single(i => i.Code == ProductSaleValidator.ProductNoRecipeTemplate);
            issue.Severity.ShouldBe(SaleReadinessSeverity.Warning);
            issue.StepKey.ShouldBe(ProductSaleValidator.StepRecipe);
            before.CanVerify.ShouldBeTrue();

            await WithUnitOfWorkAsync(async () =>
            {
                var product = await _products.GetAsync(scenario.ProductId);
                // Şablon bağı id-only; satışa hazırlık paneli yalnız "seçilmiş mi" diye bakar, katalog kaydını görmez.
                product.SetRecipeTemplate(Guid.NewGuid());
                await _products.UpdateAsync(product, autoSave: true);
            });

            var after = await _productAppService.GetSaleReadinessAsync(scenario.ProductId);
            after.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ProductNoRecipeTemplate);
        }
    }

    /// <summary>
    /// ④ GÖRSELİ YALNIZ VARYANTTA OLAN ÜRÜN "görselsiz" SAYILMAZ (2026-08-21 onarımı).
    ///
    /// <para><b>Sabitlenen hata:</b> panel görselleri yalnız KAYIT-GENELİ bağlamdan sayıyordu
    /// (<c>GetPushMediaAsync("Product", …)</c>), oysa push <see cref="MarketplacePushImageResolver"/> ile
    /// varyant → kayıt fallback'li okuyor. Fotoğraflarını yalnız varyant panelinden ekleyen kullanıcı, gerçekte
    /// gönderilebilir bir üründe KALICI "Ürünün görseli yok" uyarısı ve "Başlanmadı" adımı görüyordu.</para>
    ///
    /// <para>İki yönü birden çivileniyor: issue ÇIKMAMALI ve satır "Gönder"e AÇIK olmalı — ikisi artık aynı
    /// kümeden türüyor, yani panel "0 görsel" dediğinde push da gerçekten <c>ImagesRequired</c> ile düşer.</para>
    /// </summary>
    [Fact]
    public async Task Push_is_offered_when_the_only_images_live_on_a_variant()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var scenario = await SeedProductAsync(companyId, "RDY4", priceSecondVariant: true);

            var mediaId = await UploadPixelAsync("RDY4-varyant.png");
            await WithUnitOfWorkAsync(() => _entityMedia.ReplaceForAsync(
                MediaEntityNames.ProductVariant, scenario.VariantIds[0], companyId,
                new List<EntityMediaLinkEditDto> { new() { MediaId = mediaId, DisplayOrder = 0, IsDefault = true, IsActive = true } }));

            var channel = await WithUnitOfWorkAsync(() => _trendyolChannels.InsertAsync(
                new SalesChannelTrTrendyol(companyId, "TY-RDY4", "Trendyol RDY4", "seller-4", "api-key", "api-secret"),
                autoSave: true));
            await WithUnitOfWorkAsync(() => _trendyolProducts.InsertAsync(
                new SalesChannelTrTrendyolProduct(companyId, channel.Id, scenario.ProductId, "MAIN-RDY4", 1, "cat-1", "brand-1"),
                autoSave: true));

            var n11Channel = await WithUnitOfWorkAsync(() => _n11Channels.InsertAsync(
                new SalesChannelTrN11(companyId, "N11-RDY4", "N11 RDY4", "app-key", "app-secret"),
                autoSave: true));
            await WithUnitOfWorkAsync(async () =>
            {
                var n11 = new SalesChannelTrN11Product(
                    companyId, n11Channel.Id, scenario.ProductId, "MAIN-RDY4", 1, "cat-1", "Sablon");
                n11.Skus.Add(new SalesChannelTrN11ProductSku(scenario.VariantIds[0], "MAIN-RDY4-1"));

                // PASİF kayıt: CanSyncStockPrice'ın IsActive konjonktu (Trendyol ile hizalama) buradan pinlenir —
                // 15 dk'lık turun pasif kayda yazmaya devam ettiği asimetrinin panel yüzü.
                n11.SetActive(false);
                await _n11Products.InsertAsync(n11, autoSave: true);
            });

            var readiness = await _productAppService.GetSaleReadinessAsync(scenario.ProductId);

            readiness.ImageCount.ShouldBe(1, "Sayım push'un çözücüsünden gelmeli — varyant görseli de görseldir.");
            readiness.Issues.ShouldNotContain(i => i.Code == ProductSaleValidator.ProductNoImage);
            readiness.Steps.Single(s => s.Key == ProductSaleValidator.StepImages)
                .State.ShouldNotBe(SaleReadinessStepState.NotStarted);
            readiness.Channels.Count.ShouldBe(2);
            readiness.Channels.Single(r => r.ChannelType == SalesChannelType.TrTrendyol).CanPush.ShouldBeTrue();

            var n11Row = readiness.Channels.Single(r => r.ChannelType == SalesChannelType.TrN11);
            n11Row.CanPush.ShouldBeTrue("Görsel var — N11 push'u açılır (pasiflik push'u değil senkronu kapatır).");
            n11Row.CanSyncStockPrice.ShouldBeFalse("PASİF kayıtta senkron kapalı — SKU olsa bile.");
        }
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>1×1 PNG'yi kütüphaneye yükler — bağ MediaId ile kurulur (içerik değişmezdir, yeniden yükleme yok).</summary>
    private async Task<Guid> UploadPixelAsync(string fileName)
    {
        var content = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

        var media = await WithUnitOfWorkAsync(() => _media.UploadAsync(new MediaUploadDto
        {
            FileName = fileName,
            Content = content,
        }));

        return media.Id;
    }

    private async Task<ReadinessScenario> SeedProductAsync(Guid companyId, string prefix, bool priceSecondVariant)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var product = new Product(companyId, $"{prefix}-URN", $"{prefix} Ürünü");
            product.SetStockPolicy(ProductStockPolicy.Fixed);
            product.SetProductCategory(Guid.NewGuid());   // id-only bağ: yalnız "var mı" okunur
            product.SetVatRate(20);
            await _products.InsertAsync(product, autoSave: true);

            var variantIds = new List<Guid>();
            for (var i = 1; i <= 2; i++)
            {
                var variant = new EntityVariant(
                    companyId, ProductEntityName, product.Id, $"{prefix}-V{i}", $"{prefix} Varyant {i}", isMain: i == 1);
                await _variants.InsertAsync(variant, autoSave: true);
                variantIds.Add(variant.Id);

                var line = new ProductVariantRecipeLine(companyId, variant.Id, RecipeComponentType.Service, lineOrder: 0);
                await _recipeLines.InsertAsync(line, autoSave: true);

                var detail = new ProductVariantDetail(companyId, variant.Id);
                if (i == 1 || priceSecondVariant)
                {
                    detail.SetSalePrice(500m + i, null);
                }

                await _details.InsertAsync(detail, autoSave: true);
            }

            return new ReadinessScenario(product.Id, variantIds);
        });
    }

    private sealed record ReadinessScenario(Guid ProductId, List<Guid> VariantIds);
}
