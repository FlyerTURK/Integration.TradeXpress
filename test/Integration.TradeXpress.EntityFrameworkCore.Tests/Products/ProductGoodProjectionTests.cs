using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// ÜRÜN ↔ MAMÜL PROJEKSİYONLARI — iki yönün de sözleşmesi.
///
/// <para><b>Bu ağın varlık sebebi</b> (2026-08-10 Hakan bulgusu): mamül üründen üretilirken <c>Code</c> ve
/// <c>Name</c> dışında HİÇBİR ŞEY taşınmıyordu — görseller ve varyantlar kayboluyor, üstelik varyant grafı
/// boş gittiği için ana varyant <c>ANAVARYANT</c> sentinel koduyla doğuyordu. O kod pazaryerine SKU olarak
/// gidebildiğinden sessiz değil PAHALI bir hatadır.
///
/// <para><b>Sentinel ayrıca sabitleniyor:</b> "varyant sayısı doğru" assert'i tek başına geçerken kodların
/// yanlış olması mümkündü — iki ayrı iddia gerekiyor.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class ProductGoodProjectionTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string ProductEntityName = "Product";
    private const string GoodEntityName = "Good";

    private readonly ProductToGoodProjector _toGood;
    private readonly GoodToProductProjector _toProduct;
    private readonly IRepository<Product, Guid> _products;
    private readonly IRepository<Good, Guid> _goods;
    private readonly IRepository<EntityVariant, Guid> _variants;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public ProductGoodProjectionTests()
    {
        _toGood         = GetRequiredService<ProductToGoodProjector>();
        _toProduct      = GetRequiredService<GoodToProductProjector>();
        _products       = GetRequiredService<IRepository<Product, Guid>>();
        _goods          = GetRequiredService<IRepository<Good, Guid>>();
        _variants       = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _seeder         = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    // ── Ürün → Mamül ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Projecting_a_product_carries_its_variants_instead_of_recreating_them()
    {
        // Varyantlar TAŞINIR: kartezyeni yeniden kurmak, kullanıcının üründe yaptığı elemeleri geri getirirdi.
        var companyId = await NewCompanyAsync("P2G");
        var productId = await SeedProductAsync(companyId, "URN-P2G", variantCount: 3);

        var projected = await WithUnitOfWorkAsync(() => _toGood.ProjectAsync(productId));

        projected.Code.ShouldBe("URN-P2G");
        projected.Variants.Count.ShouldBe(3);
        projected.Variants.Select(v => v.Code)
            .ShouldBe(new[] { "URN-P2G-V1", "URN-P2G-V2", "URN-P2G-V3" }, ignoreOrder: true);

        // Ana varyant KORUNUR — hangi varyantın ana olduğu bir karardır, yeniden atanacak bir şey değil.
        projected.Variants.Count(v => v.IsMain).ShouldBe(1);
        projected.Variants.Single(v => v.IsMain).Code.ShouldBe("URN-P2G-V1");
    }

    [Fact]
    public async Task A_variantless_product_projects_a_main_variant_coded_after_the_record_not_the_sentinel()
    {
        // SABİTLENEN HATA: varyantsız üründe graf boş gidiyor ve ana varyant "ANAVARYANT" ile doğuyordu.
        // Tek varyant bir AYRIM değildir; ayırt edici bir kod taşımasının anlamı yok, kaydın kodunu izler.
        var companyId = await NewCompanyAsync("P2G-BOS");
        var productId = await SeedProductAsync(companyId, "URN-P2G-BOS", variantCount: 0);

        var projected = await WithUnitOfWorkAsync(() => _toGood.ProjectAsync(productId));

        var main = projected.Variants.ShouldHaveSingleItem();
        main.IsMain.ShouldBeTrue();
        main.Code.ShouldBe("URN-P2G-BOS");
        main.Code.ShouldNotBe(EntityVariantConsts.MainVariantCode);
    }

    // ── Mamül → Ürün ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Projecting_a_good_carries_identity_and_variants_back_to_a_product()
    {
        var companyId = await NewCompanyAsync("G2P");
        var goodId = await SeedGoodAsync(companyId, "MAM-G2P", variantCount: 2);

        var projected = await WithUnitOfWorkAsync(() => _toProduct.ProjectAsync(goodId));

        projected.Code.ShouldBe("MAM-G2P");
        projected.Name.ShouldBe("MAM-G2P Mamülü");
        projected.Variants.Count.ShouldBe(2);
        projected.Variants.Select(v => v.Code)
            .ShouldBe(new[] { "MAM-G2P-V1", "MAM-G2P-V2" }, ignoreOrder: true);
        projected.Variants.Count(v => v.IsMain).ShouldBe(1);
    }

    [Fact]
    public async Task Projecting_a_good_does_not_carry_price_because_the_product_derives_it_from_its_recipe()
    {
        // İLERİ YÖNLE ARASINDAKİ EN ÖNEMLİ FARK ve kasıtlıdır: mamülde fiyat varyantta YAŞAR, üründe ise
        // reçeteden türetilen maliyetin üzerine kurulur. Mamülün fiyatını ürünün satış fiyatına yazmak
        // maliyeti fiyat sanmak olurdu; orkestrasyon o değeri zaten sessizce ezerdi.
        var companyId = await NewCompanyAsync("G2P-FYT");
        var goodId = await SeedGoodAsync(companyId, "MAM-G2P-FYT", variantCount: 1);

        var projected = await WithUnitOfWorkAsync(() => _toProduct.ProjectAsync(goodId));

        projected.Variants.ShouldAllBe(v => v.SalePrice == null);
        projected.Variants.ShouldAllBe(v => v.RecipeLines.Count == 0);
    }

    [Fact]
    public async Task A_variantless_good_projects_a_main_variant_coded_after_the_record_not_the_sentinel()
    {
        // Sentinel kuralı İKİ YÖNDE de geçerli — ters yön yeni yazıldığı için aynı tuzağa düşmesi kolaydı.
        var companyId = await NewCompanyAsync("G2P-BOS");
        var goodId = await SeedGoodAsync(companyId, "MAM-G2P-BOS", variantCount: 0);

        var projected = await WithUnitOfWorkAsync(() => _toProduct.ProjectAsync(goodId));

        var main = projected.Variants.ShouldHaveSingleItem();
        main.IsMain.ShouldBeTrue();
        main.Code.ShouldBe("MAM-G2P-BOS");
        main.Code.ShouldNotBe(EntityVariantConsts.MainVariantCode);
    }

    // ── Kurulum yardımcıları ────────────────────────────────────────────────────────────────────────

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
            var product = new Product(companyId, code, $"{code} Ürünü");
            await _products.InsertAsync(product, autoSave: true);

            for (var i = 1; i <= variantCount; i++)
            {
                await _variants.InsertAsync(
                    new EntityVariant(companyId, ProductEntityName, product.Id, $"{code}-V{i}", $"{code} Varyant {i}", isMain: i == 1),
                    autoSave: true);
            }

            return product.Id;
        });
    }

    private Task<Guid> SeedGoodAsync(Guid companyId, string code, int variantCount)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var good = new Good(code, $"{code} Mamülü", companyId);
            await _goods.InsertAsync(good, autoSave: true);

            for (var i = 1; i <= variantCount; i++)
            {
                await _variants.InsertAsync(
                    new EntityVariant(companyId, GoodEntityName, good.Id, $"{code}-V{i}", $"{code} Varyant {i}", isMain: i == 1),
                    autoSave: true);
            }

            return good.Id;
        });
    }
}
