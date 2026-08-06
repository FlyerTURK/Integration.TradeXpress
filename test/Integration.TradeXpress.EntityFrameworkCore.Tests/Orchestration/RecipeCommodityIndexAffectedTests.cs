using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// <see cref="RecipeCommodityIndex.FindAffectedProductsAsync"/> — STOK yeniden-hesabının ters-endeksi
/// (emtia stoğu değişti → hangi ürünler yeniden hesaplanmalı).
///
/// <para><b>2026-08-06 genişlemesi:</b> sorgu <c>CommodityProcessType == Metal</c>'e SABİTLENMİŞTİ. Mamül
/// (<c>Good</c>) reçete satırı taşıyan ürün, mamül stoğu değişse bile HİÇ uyanmıyordu — kanal stoğu eski
/// kalıyor, yani aşırı satış kapısı o ailede tamamen açıktı. Aile artık anahtarın parçası.</para>
///
/// <para><b>⚠ UoW ZORUNLU</b> — gerekçesi <see cref="RecipeCommodityIndexUsageTests"/> sınıf yorumunda.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class RecipeCommodityIndexAffectedTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string ProductVariantEntityName = "Product";

    private readonly RecipeCommodityIndex _index;
    private readonly IRepository<Product, Guid> _products;
    private readonly IRepository<EntityVariant, Guid> _variants;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLines;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;

    public RecipeCommodityIndexAffectedTests()
    {
        _index          = GetRequiredService<RecipeCommodityIndex>();
        _products       = GetRequiredService<IRepository<Product, Guid>>();
        _variants       = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _recipeLines    = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
        _currentTenant  = GetRequiredService<ICurrentTenant>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    /// <summary>ASIL YENİLİK: mamül stoğu değişince mamül reçeteli ürün de uyanır.</summary>
    [Fact]
    public async Task Good_stock_change_wakes_products_with_good_recipe_lines()
    {
        await InCompanyAsync(async companyId =>
        {
            var goodId = SimpleGuidGenerator.Instance.Create();
            var productId = await SeedCalculatedProductAsync(companyId, "GD-1", ProcessType.Good, goodId);

            var affected = await WithUnitOfWorkAsync(
                () => _index.FindAffectedProductsAsync(
                    new[] { new CommodityStockKey(ProcessType.Good, goodId, null) }));

            affected.Select(a => a.ProductId).ShouldContain(productId);
        });
    }

    /// <summary>Maden yolu AYNEN çalışmayı sürdürür — genişleme mevcut davranışı bozmamalı.</summary>
    [Fact]
    public async Task Metal_stock_change_still_wakes_products_with_metal_recipe_lines()
    {
        await InCompanyAsync(async companyId =>
        {
            var metalId = SimpleGuidGenerator.Instance.Create();
            var productId = await SeedCalculatedProductAsync(companyId, "MT-1", ProcessType.Metal, metalId);

            var affected = await WithUnitOfWorkAsync(
                () => _index.FindAffectedProductsAsync(
                    new[] { new CommodityStockKey(ProcessType.Metal, metalId, null) }));

            affected.Select(a => a.ProductId).ShouldContain(productId);
        });
    }

    /// <summary>AİLE ANAHTARIN PARÇASI: aynı Guid başka ailede geldiğinde ürün UYANMAZ.
    /// <para><c>CommodityId</c> FK'sız snapshot — çakışma gerçek bir ihtimal. Aile filtresi düşerse
    /// alakasız bir ürünün stoğu yeniden hesaplanır ve yanlış adet kanala gider.</para></summary>
    [Fact]
    public async Task Same_id_in_another_family_does_not_wake_the_product()
    {
        await InCompanyAsync(async companyId =>
        {
            var sharedId = SimpleGuidGenerator.Instance.Create();
            await SeedCalculatedProductAsync(companyId, "MT-2", ProcessType.Metal, sharedId);

            var affected = await WithUnitOfWorkAsync(
                () => _index.FindAffectedProductsAsync(
                    new[] { new CommodityStockKey(ProcessType.Good, sharedId, null) }));

            affected.ShouldBeEmpty();
        });
    }

    /// <summary>KARMA olayda çapraz eşleşme olmaz: (Metal, A) + (Good, B) geldiğinde reçetesinde
    /// (Metal, B) taşıyan ürün uyanMAMALI. SQL iki listeyi bağımsız filtreler; ayıklama bellekte
    /// (aile, emtia) çiftiyle yapılır — bu test o ayıklamanın pinidir.</summary>
    [Fact]
    public async Task Mixed_event_does_not_cross_match_family_and_commodity()
    {
        await InCompanyAsync(async companyId =>
        {
            var metalA = SimpleGuidGenerator.Instance.Create();
            var goodB  = SimpleGuidGenerator.Instance.Create();

            // Ürünün reçetesi (Metal, goodB) taşıyor — olayda geçen çiftlerin HİÇBİRİ bu değil.
            await SeedCalculatedProductAsync(companyId, "MX-1", ProcessType.Metal, goodB);

            var affected = await WithUnitOfWorkAsync(
                () => _index.FindAffectedProductsAsync(new[]
                {
                    new CommodityStockKey(ProcessType.Metal, metalA, null),
                    new CommodityStockKey(ProcessType.Good, goodB, null),
                }));

            affected.ShouldBeEmpty();
        });
    }

    /// <summary>Fixed stok politikası ELENMEYE devam eder — orkestratör elle girilen stoğa dokunmaz
    /// (Hakan kararı). Kullanım sorgusundan (FindUsageAsync) ayrıldığı yer tam burasıdır.</summary>
    [Fact]
    public async Task Fixed_stock_products_are_still_excluded_from_recalculation()
    {
        await InCompanyAsync(async companyId =>
        {
            var goodId = SimpleGuidGenerator.Instance.Create();
            await SeedProductAsync(companyId, "GD-FIX", ProductStockPolicy.Fixed, ProcessType.Good, goodId);

            var affected = await WithUnitOfWorkAsync(
                () => _index.FindAffectedProductsAsync(
                    new[] { new CommodityStockKey(ProcessType.Good, goodId, null) }));

            affected.ShouldBeEmpty();
        });
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    private Task<Guid> SeedCalculatedProductAsync(
        Guid companyId, string code, ProcessType family, Guid commodityId)
    {
        return SeedProductAsync(companyId, code, ProductStockPolicy.Calculated, family, commodityId);
    }

    private Task<Guid> SeedProductAsync(
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
