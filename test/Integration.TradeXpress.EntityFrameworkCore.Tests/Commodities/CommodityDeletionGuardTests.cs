using System;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.RecipeTemplates;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Commodities;

/// <summary>
/// <b>EMTİA SİLME GUARD'I</b> (2026-08-05 Hakan kararı: *"Silinmiş olan bir emtianın satışa sunulması gibi
/// bir şey yok"*) — reçetede kullanılan emtia silinemez; kullanıcı önce reçeteleri temizler.
///
/// <para><b>Guard neden ara tabanda:</b> <see cref="CommodityCatalogAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/>
/// yedi emtia ailesinin ortak atasıdır; guard tek yere yazılır. Bu testler <c>Good</c> üzerinden koşar ama
/// korudukları şey TÜM aileler için geçerli olan taban davranıştır — türevden biri tabanı bypass ederse
/// (kendi <c>DeleteAsync</c>'ini override edip <c>base</c>'i çağırmazsa) buradan görülür.</para>
///
/// <para><b>Şablon istisnası:</b> reçete ŞABLONU taslaktır, canlı satış değildir → silmeyi BLOKLAMAZ.
/// Bu ayrım bilinçlidir ve testle kilitlenir: aksi halde kullanılmayan bir şablon yüzünden emtia
/// silinemez hale gelirdi.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class CommodityDeletionGuardTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string ProductVariantEntityName = "Product";

    private readonly IGoodAppService _goodAppService;
    private readonly IRepository<Good, Guid> _goods;
    private readonly IRepository<Product, Guid> _products;
    private readonly IRepository<EntityVariant, Guid> _variants;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLines;
    private readonly IRepository<ProductVariantDetail, Guid> _details;
    private readonly IRepository<RecipeTemplate, Guid> _templates;
    private readonly IRepository<RecipeTemplateLine, Guid> _templateLines;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;

    public CommodityDeletionGuardTests()
    {
        _goodAppService = GetRequiredService<IGoodAppService>();
        _goods = GetRequiredService<IRepository<Good, Guid>>();
        _products = GetRequiredService<IRepository<Product, Guid>>();
        _variants = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _recipeLines = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
        _details = GetRequiredService<IRepository<ProductVariantDetail, Guid>>();
        _templates = GetRequiredService<IRepository<RecipeTemplate, Guid>>();
        _templateLines = GetRequiredService<IRepository<RecipeTemplateLine, Guid>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    /// <summary>ASIL KURAL: ürün reçetesinde kullanılan emtia SİLİNEMEZ. Guard düşerse silme sessizce
    /// geçer ve o reçete çözülemeyen bir emtiaya bakar kalır (bugün düzeltilen "sessiz sıfır" sınıfı).</summary>
    [Fact]
    public async Task Commodity_used_in_a_product_recipe_cannot_be_deleted()
    {
        await InCompanyAsync(async companyId =>
        {
            var goodId = await SeedGoodAsync(companyId, "GRD-1");
            await SeedProductWithCommodityLineAsync(companyId, "URN-1", ProcessType.Good, goodId);

            var ex = await Should.ThrowAsync<BusinessException>(() => _goodAppService.DeleteAsync(goodId));

            ex.Code.ShouldBe("TradeXpress:Commodity:InUseByRecipes");

            // Kayıt GERÇEKTEN duruyor — exception fırlatıp yine de silmek en kötü sonuç olurdu.
            (await WithUnitOfWorkAsync(() => _goods.FindAsync(goodId))).ShouldNotBeNull();
        });
    }

    /// <summary>Kullanılmayan emtia normal şekilde silinebilir — guard "her şeyi blokla"ya dönüşmemeli.</summary>
    [Fact]
    public async Task Unused_commodity_can_still_be_deleted()
    {
        await InCompanyAsync(async companyId =>
        {
            var goodId = await SeedGoodAsync(companyId, "GRD-2");

            await _goodAppService.DeleteAsync(goodId);

            (await WithUnitOfWorkAsync(() => _goods.FindAsync(goodId))).ShouldBeNull();
        });
    }

    /// <summary>ŞABLON kullanımı silmeyi BLOKLAMAZ (Hakan: *"şablonda uyarı yeter"*). Şablon taslaktır;
    /// kullanılmayan bir şablon yüzünden emtia silinememesi orantısız olurdu.</summary>
    [Fact]
    public async Task Commodity_used_only_in_a_recipe_template_can_be_deleted()
    {
        await InCompanyAsync(async companyId =>
        {
            var goodId = await SeedGoodAsync(companyId, "GRD-3");
            await SeedTemplateWithCommodityLineAsync(companyId, "ŞBL-GRD", ProcessType.Good, goodId);

            await _goodAppService.DeleteAsync(goodId);

            (await WithUnitOfWorkAsync(() => _goods.FindAsync(goodId))).ShouldBeNull();
        });
    }

    /// <summary>AİLE FİLTRESİ: aynı Guid başka ailede reçetede geçiyorsa Good silmesi engellenmemeli.
    /// <c>CommodityId</c> FK'sız snapshot olduğu için bu çakışma gerçek bir ihtimaldir.</summary>
    [Fact]
    public async Task Usage_in_another_commodity_family_does_not_block_deletion()
    {
        await InCompanyAsync(async companyId =>
        {
            var goodId = await SeedGoodAsync(companyId, "GRD-4");

            // AYNI id, ama reçete satırı Metal ailesinde yazılmış.
            await SeedProductWithCommodityLineAsync(companyId, "URN-4", ProcessType.Metal, goodId);

            await _goodAppService.DeleteAsync(goodId);

            (await WithUnitOfWorkAsync(() => _goods.FindAsync(goodId))).ShouldBeNull();
        });
    }

    /// <summary>
    /// <b>PASİFLEŞTİRME → KADEMELİ ASKIYA ALMA</b> (2026-08-05 Hakan kararı: *"Pasifleştirilebilir ama ürün
    /// Askıda'ya düşer"* + *"Varyant Askıda, tümü etkilenirse ürün de"*).
    ///
    /// <para>ASIL KURAL burada: emtiayı kullanan varyant askıya alınır, ama ürünün SAĞLAM varyantı varsa
    /// ürün satışta KALIR. Kademeyi kaçırıp ürünü komple kapatmak, ilgisiz varyantların satışını da
    /// durdururdu — gereksiz zarar.</para>
    /// </summary>
    [Fact]
    public async Task Deactivating_a_commodity_suspends_only_the_using_variant_not_the_whole_product()
    {
        await InCompanyAsync(async companyId =>
        {
            var goodId = await SeedGoodAsync(companyId, "GRD-5");

            // Aynı üründe İKİ varyant: biri emtiayı kullanıyor, diğeri kullanmıyor. İkisi de onaylı.
            var (productId, usingVariantId, cleanVariantId) =
                await SeedProductWithTwoVerifiedVariantsAsync(companyId, "URN-5", ProcessType.Good, goodId);

            await DeactivateGoodAsync(goodId);

            var usingStatus = await GetVariantStatusAsync(usingVariantId);
            var cleanStatus = await GetVariantStatusAsync(cleanVariantId);
            var productStatus = await GetProductStatusAsync(productId);

            usingStatus.ShouldBe(ProductSaleStatus.Suspended);

            // Kademe: sağlam varyant ve ürün SATIŞTA kalır.
            cleanStatus.ShouldBe(ProductSaleStatus.Ready);
            productStatus.ShouldBe(ProductSaleStatus.Ready);
        });
    }

    /// <summary>Ürünün TÜM varyantları etkilenirse ürün de askıya alınır (kademenin üst basamağı).</summary>
    [Fact]
    public async Task Product_is_suspended_when_all_of_its_variants_are()
    {
        await InCompanyAsync(async companyId =>
        {
            var goodId = await SeedGoodAsync(companyId, "GRD-6");

            // İKİ varyant da AYNI emtiayı kullanıyor.
            var (productId, first, second) =
                await SeedProductWithTwoVerifiedVariantsAsync(
                    companyId, "URN-6", ProcessType.Good, goodId, secondVariantUsesCommodity: true);

            await DeactivateGoodAsync(goodId);

            (await GetVariantStatusAsync(first)).ShouldBe(ProductSaleStatus.Suspended);
            (await GetVariantStatusAsync(second)).ShouldBe(ProductSaleStatus.Suspended);
            (await GetProductStatusAsync(productId)).ShouldBe(ProductSaleStatus.Suspended);
        });
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    private Task<Guid> SeedGoodAsync(Guid companyId, string code)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var good = await _goods.InsertAsync(new Good(code, $"{code} Ticari Mal", companyId), autoSave: true);
            return good.Id;
        });
    }

    /// <summary>İki ONAYLI varyantlı ürün. Varsayılan: yalnız BİRİNCİ varyant emtiayı kullanır — kademeli
    /// askıya almanın "sağlam varyant satışta kalır" dalını sınamak için.</summary>
    private Task<(Guid ProductId, Guid UsingVariantId, Guid OtherVariantId)> SeedProductWithTwoVerifiedVariantsAsync(
        Guid companyId, string code, ProcessType family, Guid commodityId, bool secondVariantUsesCommodity = false)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var product = new Product(companyId, code, $"{code} Ürünü");
            product.MarkSaleReady();
            await _products.InsertAsync(product, autoSave: true);

            var first = await InsertVerifiedVariantAsync(companyId, product.Id, code, "V1", isMain: true);
            var second = await InsertVerifiedVariantAsync(companyId, product.Id, code, "V2", isMain: false);

            await InsertCommodityLineAsync(companyId, first, family, commodityId);
            if (secondVariantUsesCommodity)
            {
                await InsertCommodityLineAsync(companyId, second, family, commodityId);
            }

            return (product.Id, first, second);
        });
    }

    private async Task<Guid> InsertVerifiedVariantAsync(
        Guid companyId, Guid productId, string code, string suffix, bool isMain)
    {
        var variant = new EntityVariant(
            companyId, ProductVariantEntityName, productId, $"{code}-{suffix}", $"{code} {suffix}", isMain);
        await _variants.InsertAsync(variant, autoSave: true);

        var detail = new ProductVariantDetail(companyId, variant.Id);
        detail.SetSalePrice(100m, null);
        detail.MarkVerified(RecipeVerificationStamp.EmptyRecipe, DateTime.UtcNow, verifiedBy: null);
        await _details.InsertAsync(detail, autoSave: true);

        return variant.Id;
    }

    private async Task InsertCommodityLineAsync(
        Guid companyId, Guid variantId, ProcessType family, Guid commodityId)
    {
        var line = new ProductVariantRecipeLine(
            companyId, variantId, RecipeComponentType.CatalogCommodity, lineOrder: 0);
        line.SetCatalogCommodity(
            family, commodityId, commodityVariantId: null,
            quantity: 1m, amount: 1m, factor: 1m, valuationUnitId: null,
            ProcessPaymentType.Normal, payFactor: 0m, payUnitId: null);
        await _recipeLines.InsertAsync(line, autoSave: true);
    }

    /// <summary>Emtiayı UpdateAsync üzerinden pasifleştirir — üretimdeki tek yol (ayrı endpoint yok).</summary>
    private async Task DeactivateGoodAsync(Guid goodId)
    {
        var dto = await _goodAppService.GetAsync(goodId);
        await _goodAppService.UpdateAsync(goodId, new GoodUpdateDto
        {
            Code = dto.Code,
            Name = dto.Name,
            IsActive = false,
        });
    }

    private Task<ProductSaleStatus> GetVariantStatusAsync(Guid variantId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var detail = await _details.FirstOrDefaultAsync(d => d.EntityVariantId == variantId);
            detail.ShouldNotBeNull();
            return detail!.SaleStatus;
        });
    }

    private Task<ProductSaleStatus> GetProductStatusAsync(Guid productId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var product = await _products.GetAsync(productId);
            return product.SaleStatus;
        });
    }

    private Task<Guid> SeedProductWithCommodityLineAsync(
        Guid companyId, string code, ProcessType family, Guid commodityId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var product = new Product(companyId, code, $"{code} Ürünü");
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
