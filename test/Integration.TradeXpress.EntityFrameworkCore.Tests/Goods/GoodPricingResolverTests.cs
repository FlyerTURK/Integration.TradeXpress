using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Goods;

/// <summary>
/// <see cref="IGoodPricingResolver"/> — iki çözüm kipi ve aralarındaki fark.
///
/// <para><b>Neden ikinci kip (varyant-bazlı) gerekti:</b> <c>ResolveAsync</c> bir mamül için TEK fiyat verir =
/// ANA varyantınki. Voucher/bilanço gibi Good.Id'ye referanslı tüketiciler için doğrudur, ama REÇETE satırı
/// varyant seçebilir. Ana-varyant fiyatına düşmek, farklı maliyetli varyantları (14/18/22 ayar) tek fiyata
/// çökertirdi — bu testler o çökmenin geri gelmesini engeller.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class GoodPricingResolverTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string GoodEntityName = "Good";

    private readonly IGoodPricingResolver _resolver;
    private readonly IRepository<Good, Guid> _goodRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<GoodVariantDetail, Guid> _detailRepository;
    private readonly ICurrentCompany _currentCompany;

    public GoodPricingResolverTests()
    {
        _resolver = GetRequiredService<IGoodPricingResolver>();
        _goodRepository = GetRequiredService<IRepository<Good, Guid>>();
        _variantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _detailRepository = GetRequiredService<IRepository<GoodVariantDetail, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    /// <summary>Mamül-bazlı çözüm ANA varyantın fiyatını verir — ikinci varyantın fiyatı ne olursa olsun.</summary>
    [Fact]
    public async Task Good_level_resolution_returns_the_main_variant_price()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (goodId, _, _) = await SeedGoodWithTwoPricedVariantsAsync(companyId, mainPrice: 1000m, otherPrice: 2500m);

            var result = await WithUnitOfWorkAsync(async () => await _resolver.ResolveAsync(new[] { goodId }));

            result[goodId].EntryPrice.ShouldBe(1000m);
        }
    }

    /// <summary>Varyant-bazlı çözüm HER varyantın KENDİ fiyatını verir. Bu geçmezse reçetede seçilen varyant
    /// ne olursa olsun ana varyantın maliyeti kullanılır.</summary>
    [Fact]
    public async Task Variant_level_resolution_returns_each_variants_own_price()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (_, mainVariantId, otherVariantId) =
                await SeedGoodWithTwoPricedVariantsAsync(companyId, mainPrice: 1000m, otherPrice: 2500m);

            var result = await WithUnitOfWorkAsync(async () =>
                await _resolver.ResolveByVariantAsync(new[] { mainVariantId, otherVariantId }));

            result[mainVariantId].EntryPrice.ShouldBe(1000m);
            result[otherVariantId].EntryPrice.ShouldBe(2500m);
        }
    }

    /// <summary>Fiyat detayı OLMAYAN varyant sözlükte YOKTUR — çağıran (populator) bunu ana-varyant
    /// fallback'ine düşme sinyali olarak kullanır. Sözlüğe 0 yazmak "fiyat sıfır" demek olurdu.</summary>
    [Fact]
    public async Task Variant_without_a_price_detail_is_absent_rather_than_zero()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (_, mainVariantId, _) = await SeedGoodWithTwoPricedVariantsAsync(companyId, mainPrice: 1000m, otherPrice: 2500m);
            var detaillessVariantId = await AddVariantAsync(companyId, await GoodIdOfAsync(mainVariantId), "DETAYSIZ", isMain: false);

            var result = await WithUnitOfWorkAsync(async () =>
                await _resolver.ResolveByVariantAsync(new[] { mainVariantId, detaillessVariantId }));

            result.ShouldContainKey(mainVariantId);
            result.ShouldNotContainKey(detaillessVariantId);
        }
    }

    [Fact]
    public async Task Empty_input_returns_an_empty_result_without_querying()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            (await _resolver.ResolveByVariantAsync(Array.Empty<Guid>())).ShouldBeEmpty();
        }
    }

    // ── Seed ────────────────────────────────────────────────────────────────────────────────────────

    private async Task<(Guid GoodId, Guid MainVariantId, Guid OtherVariantId)> SeedGoodWithTwoPricedVariantsAsync(
        Guid companyId, decimal mainPrice, decimal otherPrice)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var code = "TICARI-" + Guid.NewGuid().ToString("N")[..8];
            var good = await _goodRepository.InsertAsync(new Good(code, $"Ticari Mal {code}", companyId), autoSave: true);

            var mainVariantId = await InsertVariantAsync(companyId, good.Id, "ANA", isMain: true, price: mainPrice);
            var otherVariantId = await InsertVariantAsync(companyId, good.Id, "DIGER", isMain: false, price: otherPrice);

            return (good.Id, mainVariantId, otherVariantId);
        });
    }

    private async Task<Guid> InsertVariantAsync(Guid companyId, Guid goodId, string code, bool isMain, decimal price)
    {
        var variant = await _variantRepository.InsertAsync(
            new EntityVariant(companyId, GoodEntityName, goodId, code, $"Varyant {code}", isMain), autoSave: true);

        var detail = new GoodVariantDetail(companyId, variant.Id);
        detail.SetPurchasePrice(price, entryPriceUnitId: null, taxIncluded: false);
        await _detailRepository.InsertAsync(detail, autoSave: true);

        return variant.Id;
    }

    private async Task<Guid> AddVariantAsync(Guid companyId, Guid goodId, string code, bool isMain)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var variant = await _variantRepository.InsertAsync(
                new EntityVariant(companyId, GoodEntityName, goodId, code, $"Varyant {code}", isMain), autoSave: true);
            return variant.Id;
        });
    }

    private async Task<Guid> GoodIdOfAsync(Guid variantId)
    {
        return await WithUnitOfWorkAsync(async () => (await _variantRepository.GetAsync(variantId)).EntityId);
    }
}
