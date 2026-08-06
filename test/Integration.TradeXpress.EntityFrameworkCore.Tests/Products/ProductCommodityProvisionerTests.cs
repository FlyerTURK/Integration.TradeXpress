using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// SİHİRBAZ SINIFLANDIRMASI — <see cref="ProductCommodityProvisioner"/> uçtan uca.
///
/// <para><b>Neden entegrasyon testi:</b> provisioner'ın işi TAM OLARAK katmanlar arası — katalog app
/// service'ini çağırır (şirket damgası + kod benzersizliği orada), reçete yazarını çağırır, ürünün stok
/// politikasını değiştirir. Birim testi bu zincirin hiçbir halkasını gerçekten sınamazdı; bu oturumda
/// iki kez (Faz 2 guard'ı, Faz 3 pasifleştirme algısı) kaynakta doğru görünen kod davranışta sessizce
/// baypas edilmişti ve ikisini de yalnız entegrasyon testi yakaladı.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class ProductCommodityProvisionerTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string ProductEntityName = "Product";

    private readonly ProductCommodityProvisioner _provisioner;
    private readonly IRepository<Product, Guid> _products;
    private readonly IRepository<EntityVariant, Guid> _variants;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLines;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public ProductCommodityProvisionerTests()
    {
        _provisioner    = GetRequiredService<ProductCommodityProvisioner>();
        _products       = GetRequiredService<IRepository<Product, Guid>>();
        _variants       = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _recipeLines    = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
        _seeder         = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
        _asyncExecuter  = GetRequiredService<IAsyncQueryableExecuter>();
    }

    /// <summary>ASIL AKIŞ: mamül sınıflandırması → katalog kaydı + reçete satırı + <c>Calculated</c> politika.
    /// <para><b>Ürün <c>Draft</c> KALIR</b> — sınıflandırma satışa açmaz; doğrulama insan işidir.</para></summary>
    [Fact]
    public async Task Provisioning_creates_commodity_and_recipe_line_and_leaves_product_draft()
    {
        var companyId = await NewCompanyAsync("PRV");
        var productId = await SeedProductAsync(companyId, "URN-PRV-1", variantCount: 2);

        var result = await WithUnitOfWorkAsync(() => _provisioner.ProvisionAsync(new ProductCommodityProvisionInputDto
        {
            Items = new List<ProductCommodityProvisionItemDto>
            {
                new()
                {
                    ProductId = productId,
                    Family    = ProcessType.Good,
                    Mode      = ProductCommodityProvisionMode.CreateNew,
                    Quantity  = 1m,
                },
            },
        }));

        result.Issues.ShouldBeEmpty();
        result.ProvisionedProducts.ShouldBe(1);
        result.CreatedCommodities.ShouldBe(1);
        result.CreatedRecipeLines.ShouldBe(2);   // ürünün HER varyantına satır yazılır

        var lines = await WithUnitOfWorkAsync(() => LoadLinesAsync(productId));
        lines.Count.ShouldBe(2);
        lines.ShouldAllBe(l => l.CommodityProcessType == ProcessType.Good);
        lines.ShouldAllBe(l => l.CommodityId != null);
        lines.ShouldAllBe(l => l.Quantity == 1m);

        var product = await WithUnitOfWorkAsync(() => _products.GetAsync(productId));
        product.StockPolicy.ShouldBe(ProductStockPolicy.Calculated);

        // ASIL KURAL: sınıflandırma satışa AÇMAZ — güvenlik statüden gelir, zorunluluktan değil.
        product.SaleStatus.ShouldBe(ProductSaleStatus.Draft);
    }

    /// <summary>Yalnız HİZMET satırı olan ürün <c>Unlimited</c> olur (2026-08-05 karar #7).
    /// <para><c>Calculated</c> yapmak, stok zincirinin hiç veri bulamayacağı bir hesap açardı ve sonuç
    /// sessizce 0'a düşerdi — ürün sebepsizce satıştan kalkardı.</para></summary>
    [Fact]
    public async Task Service_only_product_becomes_unlimited_not_calculated()
    {
        var companyId = await NewCompanyAsync("PRS");
        var productId = await SeedProductAsync(companyId, "URN-PRS-1", variantCount: 1);

        var result = await WithUnitOfWorkAsync(() => _provisioner.ProvisionAsync(new ProductCommodityProvisionInputDto
        {
            Items = new List<ProductCommodityProvisionItemDto>
            {
                new() { ProductId = productId, Family = ProcessType.Service, Mode = ProductCommodityProvisionMode.CreateNew },
            },
        }));

        result.Issues.ShouldBeEmpty();
        result.ProvisionedProducts.ShouldBe(1);

        var product = await WithUnitOfWorkAsync(() => _products.GetAsync(productId));
        product.StockPolicy.ShouldBe(ProductStockPolicy.Unlimited);

        var lines = await WithUnitOfWorkAsync(() => LoadLinesAsync(productId));
        var line = lines.ShouldHaveSingleItem();
        line.ComponentType.ShouldBe(RecipeComponentType.Service);

        // Hizmet satırı aile kolonunu DOLDURMAZ — stok zincirine hiç girmemesinin mekanizması budur.
        line.CommodityProcessType.ShouldBeNull();
    }

    /// <summary>Doğal birim ZORUNLU olan ailede (Metal/Scrap/Future) birim seçilmemişse SESSİZ varsayılan
    /// konmaz: emtia açılmaz, gerekçe rapora yazılır. Varsayılan bir birim uydurmak, madenin neyi takip
    /// ettiğini yanlış kurar ve tüm değerlemeyi sessizce kaydırırdı.</summary>
    [Fact]
    public async Task Metal_without_following_unit_is_reported_not_silently_defaulted()
    {
        var companyId = await NewCompanyAsync("PRM");
        var productId = await SeedProductAsync(companyId, "URN-PRM-1", variantCount: 1);

        var result = await WithUnitOfWorkAsync(() => _provisioner.ProvisionAsync(new ProductCommodityProvisionInputDto
        {
            Items = new List<ProductCommodityProvisionItemDto>
            {
                new()
                {
                    ProductId = productId,
                    Family    = ProcessType.Metal,
                    Mode      = ProductCommodityProvisionMode.CreateNew,
                    Amount    = 5m,
                },
            },
        }));

        result.ProvisionedProducts.ShouldBe(0);
        result.CreatedCommodities.ShouldBe(0);
        result.Issues.ShouldNotBeEmpty();

        var lines = await WithUnitOfWorkAsync(() => LoadLinesAsync(productId));
        lines.ShouldBeEmpty();

        // Politika DEĞİŞMEZ: yarım kalan sınıflandırma ürünü Calculated'a çevirip stoksuz bırakamaz.
        var product = await WithUnitOfWorkAsync(() => _products.GetAsync(productId));
        product.StockPolicy.ShouldBe(ProductStockPolicy.Fixed);
    }

    /// <summary>Reçetesi OLAN ürüne dokunulmaz — kullanıcının emeği toplu işlemle ezilemez.</summary>
    [Fact]
    public async Task Product_that_already_has_a_recipe_is_skipped()
    {
        var companyId = await NewCompanyAsync("PRE");
        var productId = await SeedProductAsync(companyId, "URN-PRE-1", variantCount: 1);

        var input = new ProductCommodityProvisionInputDto
        {
            Items = new List<ProductCommodityProvisionItemDto>
            {
                new()
                {
                    ProductId = productId,
                    Family    = ProcessType.Good,
                    Mode      = ProductCommodityProvisionMode.CreateNew,
                    Quantity  = 1m,
                },
            },
        };

        await WithUnitOfWorkAsync(() => _provisioner.ProvisionAsync(input));
        var second = await WithUnitOfWorkAsync(() => _provisioner.ProvisionAsync(input));

        second.ProvisionedProducts.ShouldBe(0);
        second.Issues.ShouldNotBeEmpty();

        // İkinci geçiş satır EKLEMEDİ (aksi halde reçete ikizlenir ve maliyet iki katına çıkardı).
        var lines = await WithUnitOfWorkAsync(() => LoadLinesAsync(productId));
        lines.Count.ShouldBe(1);
    }

    /// <summary>Aday listesi reçetesizleri döner ve sınıflandırıldıktan sonra listeden DÜŞER — sihirbazın
    /// "kalan iş" sayısı buradan gelir.</summary>
    [Fact]
    public async Task Candidates_exclude_products_that_were_just_classified()
    {
        var companyId = await NewCompanyAsync("PRC");
        var productId = await SeedProductAsync(companyId, "URN-PRC-1", variantCount: 1);

        var before = await WithUnitOfWorkAsync(() => _provisioner.GetCandidatesAsync());
        before.Select(c => c.ProductId).ShouldContain(productId);
        before.First(c => c.ProductId == productId).VariantCount.ShouldBe(1);

        await WithUnitOfWorkAsync(() => _provisioner.ProvisionAsync(new ProductCommodityProvisionInputDto
        {
            Items = new List<ProductCommodityProvisionItemDto>
            {
                new()
                {
                    ProductId = productId,
                    Family    = ProcessType.Good,
                    Mode      = ProductCommodityProvisionMode.CreateNew,
                    Quantity  = 1m,
                },
            },
        }));

        var after = await WithUnitOfWorkAsync(() => _provisioner.GetCandidatesAsync());
        after.Select(c => c.ProductId).ShouldNotContain(productId);
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Her test kendi şirketinde koşar — emtia katalogları per-company (CLAUDE.md §6) ve kod
    /// benzersizliği şirket kapsamlıdır; şirket paylaşmak testleri birbirine bağlardı.</summary>
    private async Task<Guid> NewCompanyAsync(string prefix)
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync(prefix));
        _companyContext.CompanyId = data.CompanyId;
        return data.CompanyId;
    }

    private Task<Guid> SeedProductAsync(Guid companyId, string code, int variantCount)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            // İçe aktarımın bıraktığı hâl: Fixed + reçetesiz.
            var product = new Product(companyId, code, $"{code} Ürünü");
            product.SetStockPolicy(ProductStockPolicy.Fixed);
            await _products.InsertAsync(product, autoSave: true);

            for (var i = 1; i <= variantCount; i++)
            {
                var variant = new EntityVariant(
                    companyId, ProductEntityName, product.Id, $"{code}-V{i}", $"{code} Varyant {i}", isMain: i == 1);
                await _variants.InsertAsync(variant, autoSave: true);
            }

            return product.Id;
        });
    }

    private async Task<List<ProductVariantRecipeLine>> LoadLinesAsync(Guid productId)
    {
        var variantIds = await _asyncExecuter.ToListAsync(
            (await _variants.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == productId)
                .Select(v => v.Id));

        return await _asyncExecuter.ToListAsync(
            (await _recipeLines.GetQueryableAsync()).Where(l => variantIds.Contains(l.ProductVariantId)));
    }
}
