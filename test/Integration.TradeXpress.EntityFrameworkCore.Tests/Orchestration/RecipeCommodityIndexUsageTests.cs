using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.RecipeTemplates;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// <see cref="RecipeCommodityIndex.FindUsageAsync"/> — emtia SİLİNİRKEN/PASİFLEŞTİRİLİRKEN "bu emtia hangi
/// ürünlerde kullanılıyor?" sorusunu cevaplayan sorgu (2026-08-05 Hakan kararı: kullanıcı uyarılmalı, devam
/// ederse ürün derhal satışa kapanmalı).
///
/// <para><b>Neden ayrı testler:</b> endeksin diğer metodu (<c>FindAffectedProductsAsync</c>) STOK yeniden-hesabı
/// içindir ve <c>Calculated</c> olmayan ürünleri ELER. Kullanım sorgusunda o eleme YANLIŞTIR — Fixed stoklu ürün
/// de reçetesinde silinmiş emtia taşıyorsa satılmamalıdır. Bu ayrım tek satırlık bir filtreyle bozulabileceği
/// için testle kilitlenir.</para>
///
/// <para><b>⚠ UoW ZORUNLU:</b> <see cref="RecipeCommodityIndex"/> <c>ITransientDependency</c>'dir —
/// <c>IUnitOfWorkEnabled</c> DEĞİL — yani kendi UoW'unu AÇMAZ. <c>GetQueryableAsync()</c> kendi (çağrı-başına)
/// UoW'unda DbContext üretip queryable döndürür, o UoW kapanır, sonra <c>ToListAsync</c> DISPOSE EDİLMİŞ
/// DbContext üzerinde koşar → <c>ObjectDisposedException</c>. Üretimde bunu <c>ProductOrchestrationManager</c>
/// açıyor ("TAZE UoW ZORUNLU" yorumu orada). Bu yüzden testte HEM seed HEM sorgu
/// <see cref="TradeXpressTestBase{TStartupModule}.WithUnitOfWorkAsync{TResult}"/> ile sarılır.
/// Sıra kritik: tenant/şirket değişimi DIŞTA, UoW İÇTE.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class RecipeCommodityIndexUsageTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string ProductVariantEntityName = "Product";

    private readonly RecipeCommodityIndex _index;
    private readonly IRepository<Product, Guid> _products;
    private readonly IRepository<EntityVariant, Guid> _variants;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLines;
    private readonly IRepository<RecipeTemplate, Guid> _templates;
    private readonly IRepository<RecipeTemplateLine, Guid> _templateLines;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;

    public RecipeCommodityIndexUsageTests()
    {
        _index = GetRequiredService<RecipeCommodityIndex>();
        _products = GetRequiredService<IRepository<Product, Guid>>();
        _variants = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _recipeLines = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
        _templates = GetRequiredService<IRepository<RecipeTemplate, Guid>>();
        _templateLines = GetRequiredService<IRepository<RecipeTemplateLine, Guid>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    /// <summary>ASIL KURAL: kullanım sorgusu stok politikasına BAKMAZ. Fixed stoklu ürün de silinmiş/pasif
    /// emtiayla satılmamalı; burada eleme yapmak uyarıyı sessizce eksiltir ve ürün satışta kalır.</summary>
    [Fact]
    public async Task Usage_includes_fixed_stock_products_not_just_calculated_ones()
    {
        await InCompanyAsync(async companyId =>
        {
            var jewelryId = SimpleGuidGenerator.Instance.Create();

            var fixedProduct = await SeedProductWithCommodityLineAsync(
                companyId, "FIX-1", ProductStockPolicy.Fixed, ProcessType.Jewelry, jewelryId);
            var calculatedProduct = await SeedProductWithCommodityLineAsync(
                companyId, "CALC-1", ProductStockPolicy.Calculated, ProcessType.Jewelry, jewelryId);

            var usage = await WithUnitOfWorkAsync(
                () => _index.FindUsageAsync(ProcessType.Jewelry, new[] { jewelryId }));

            var products = usage.Where(u => u.Kind == CommodityUsageKind.ProductRecipe).ToList();
            products.Select(u => u.OwnerId).ShouldBe(
                new[] { fixedProduct, calculatedProduct }, ignoreOrder: true);

            // Uyarı metni kurulabilsin diye kod taşınır — id listesi kullanıcıya gösterilemez.
            products.Select(u => u.OwnerCode).ShouldContain("FIX-1");

            // Ürün reçetesi CANLI kullanımdır → silmeyi bloklar.
            products.ShouldAllBe(u => u.BlocksDeletion);
        });
    }

    /// <summary>Aile filtresi ZORUNLU: <c>CommodityId</c> FK'sız snapshot, aynı Guid farklı ailede çakışabilir.
    /// Filtre düşerse silinen bir mücevher, aynı id'yi taşıyan bir madenin ürününü de kapatırdı.</summary>
    [Fact]
    public async Task Usage_does_not_match_the_same_id_in_another_commodity_family()
    {
        await InCompanyAsync(async companyId =>
        {
            var sharedId = SimpleGuidGenerator.Instance.Create();

            await SeedProductWithCommodityLineAsync(
                companyId, "MTL-1", ProductStockPolicy.Calculated, ProcessType.Metal, sharedId);

            var usage = await WithUnitOfWorkAsync(
                () => _index.FindUsageAsync(ProcessType.Jewelry, new[] { sharedId }));

            usage.ShouldBeEmpty();
        });
    }

    /// <summary>
    /// <b>ŞABLON kullanımı bulunur ama silmeyi BLOKLAMAZ</b> (2026-08-05 Hakan kararı: *"şablonda uyarı yeter"*).
    /// Şablon bir taslaktır, canlı satış değildir; kullanılmayan bir şablon yüzünden emtia silinememesi
    /// orantısız olurdu. Ama GÖRÜNMEZ de olmamalı — kullanıcı nereyi temizleyeceğini bilmeli.
    /// </summary>
    [Fact]
    public async Task Template_usage_is_reported_but_does_not_block_deletion()
    {
        await InCompanyAsync(async companyId =>
        {
            var metalId = SimpleGuidGenerator.Instance.Create();
            await SeedTemplateWithCommodityLineAsync(companyId, "ŞBL-1", ProcessType.Metal, metalId);

            var usage = await WithUnitOfWorkAsync(
                () => _index.FindUsageAsync(ProcessType.Metal, new[] { metalId }));

            var template = usage.ShouldHaveSingleItem();
            template.Kind.ShouldBe(CommodityUsageKind.RecipeTemplate);
            template.OwnerName.ShouldBe("ŞBL-1");

            // ASIL KURAL: bulunur ama bloklamaz.
            template.BlocksDeletion.ShouldBeFalse();
        });
    }

    /// <summary>
    /// <b>Service ailesi AYRI kolonlarda yaşar.</b> Hizmet satırı <c>SetService</c> ile yazılır:
    /// <c>CommodityProcessType</c> <b>null</b> kalır, <c>ComponentType</c> <c>Service</c> olur. Katalog
    /// filtresinin iki kolonu da tutmaz → ayrı sorgu dalı olmazsa Service emtiası "hiç kullanılmıyor"
    /// görünür ve sert blok o ailede tamamen delinir.
    /// </summary>
    [Fact]
    public async Task Service_family_is_found_although_it_uses_different_columns()
    {
        await InCompanyAsync(async companyId =>
        {
            var serviceId = SimpleGuidGenerator.Instance.Create();
            var productId = await SeedProductWithServiceLineAsync(companyId, "SRV-1", serviceId);

            var usage = await WithUnitOfWorkAsync(
                () => _index.FindUsageAsync(ProcessType.Service, new[] { serviceId }));

            var found = usage.ShouldHaveSingleItem();
            found.Kind.ShouldBe(CommodityUsageKind.ProductRecipe);
            found.OwnerId.ShouldBe(productId);
            found.BlocksDeletion.ShouldBeTrue();
        });
    }

    /// <summary>Kullanılmayan emtia boş döner — uyarı ekranı "kullanımda" diye yanlış alarm vermemeli.</summary>
    [Fact]
    public async Task Unused_commodity_returns_no_usage()
    {
        await InCompanyAsync(async companyId =>
        {
            await SeedProductWithCommodityLineAsync(
                companyId, "OTH-1", ProductStockPolicy.Calculated, ProcessType.Metal,
                SimpleGuidGenerator.Instance.Create());

            var usage = await WithUnitOfWorkAsync(
                () => _index.FindUsageAsync(
                    ProcessType.Metal, new[] { SimpleGuidGenerator.Instance.Create() }));

            usage.ShouldBeEmpty();
        });
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Seed TEK UoW'da — üç ayrı örtük UoW yerine bir tane (kod tabanının hakim deseni:
    /// SubstitutionCalculationTests.SeedMetalAsync, GoodPricingResolverTests.SeedGood...).</summary>
    private Task<Guid> SeedProductWithCommodityLineAsync(
        Guid companyId, string code, ProductStockPolicy policy, ProcessType family, Guid commodityId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var product = new Product(companyId, code, $"{code} Ürünü");
            product.SetStockPolicy(policy);
            await _products.InsertAsync(product, autoSave: true);

            var variant = new EntityVariant(
                companyId, ProductVariantEntityName, product.Id, $"{code}-V1", $"{code} Varyant", isMain: true);
            await _variants.InsertAsync(variant, autoSave: true);

            var line = new ProductVariantRecipeLine(
                companyId, variant.Id, RecipeComponentType.CatalogCommodity, lineOrder: 0);
            line.SetCatalogCommodity(
                family, commodityId, commodityVariantId: null,
                quantity: 1m, amount: 1m, factor: 1m, valuationUnitId: null,
                ProcessPaymentType.Normal, payFactor: 0m, payUnitId: null);
            await _recipeLines.InsertAsync(line, autoSave: true);

            return product.Id;
        });
    }

    /// <summary>Ürüne HİZMET satırı ekler — katalog satırından farklı kolonlar (SetService).</summary>
    private Task<Guid> SeedProductWithServiceLineAsync(Guid companyId, string code, Guid serviceId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var product = new Product(companyId, code, $"{code} Ürünü");
            await _products.InsertAsync(product, autoSave: true);

            var variant = new EntityVariant(
                companyId, ProductVariantEntityName, product.Id, $"{code}-V1", $"{code} Varyant", isMain: true);
            await _variants.InsertAsync(variant, autoSave: true);

            var line = new ProductVariantRecipeLine(
                companyId, variant.Id, RecipeComponentType.Service, lineOrder: 0);
            line.SetService(
                serviceId, RecipeDerivedBaseMode.AllAbove, RecipeDerivedOperation.Percent, 5m, null);
            await _recipeLines.InsertAsync(line, autoSave: true);

            return product.Id;
        });
    }

    /// <summary>Reçete ŞABLONUNA katalog emtiası satırı ekler (canlı ürün değil — taslak).</summary>
    private Task<Guid> SeedTemplateWithCommodityLineAsync(
        Guid companyId, string name, ProcessType family, Guid commodityId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var template = new RecipeTemplate(companyId, name);
            await _templates.InsertAsync(template, autoSave: true);

            var line = new RecipeTemplateLine(template.Id, RecipeComponentType.CatalogCommodity, lineOrder: 0);
            line.SetCatalogCommodity(
                family, commodityId, commodityVariantId: null,
                quantity: 1m, amount: 1m, factor: 1m, valuationUnitId: null,
                ProcessPaymentType.Normal, payFactor: 0m, payUnitId: null);
            await _templateLines.InsertAsync(line, autoSave: true);

            return template.Id;
        });
    }

    /// <summary>Her test kendi tenant+şirketinde koşar — reçete satırı ICompanyOwned, sorgu şirketle daralır.</summary>
    private async Task InCompanyAsync(Func<Guid, Task> body)
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var companyId = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = companyId;
            try
            {
                await body(companyId);
            }
            finally
            {
                _companyContext.CompanyId = null;
            }
        }
    }
}
