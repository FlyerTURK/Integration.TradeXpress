using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Goods;

/// <summary>
/// "VARYANTLI HER EMTİA REÇETEDE MADEN GİBİ DAVRANIR" (2026-08-15 Hakan kararı) — reçete panelinin mamül/mücevher
/// combo'sunu besleyen yassı varyant lookup'ları.
///
/// <para><b>Sabitlenen delik:</b> Good'da fiyat VARYANTTADIR ve sunucu maliyet motoru <c>CommodityVariantId</c> ile
/// seçili varyantın fiyatını okur; ama UI emtia-seviyesi combo gösteriyor, varyant kimliği hiç yazılmıyor, satır
/// hep ana varyantın fiyatına düşüyordu. Bu lookup her varyant satırına KENDİ fiyatını taşır (Good) — Jewelry'de
/// ise fiyat paylaşılır (bilinçli kısıt), satırlar mücevherin fiyatını taşır.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class GoodVariantLookupTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IGoodAppService _goodAppService;
    private readonly IJewelryAppService _jewelryAppService;
    private readonly IRepository<Good, Guid> _goodRepository;
    private readonly IRepository<Jewelry, Guid> _jewelryRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<GoodVariantDetail, Guid> _detailRepository;
    private readonly ICurrentCompany _currentCompany;

    public GoodVariantLookupTests()
    {
        _goodAppService = GetRequiredService<IGoodAppService>();
        _jewelryAppService = GetRequiredService<IJewelryAppService>();
        _goodRepository = GetRequiredService<IRepository<Good, Guid>>();
        _jewelryRepository = GetRequiredService<IRepository<Jewelry, Guid>>();
        _variantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _detailRepository = GetRequiredService<IRepository<GoodVariantDetail, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task Good_variant_lookup_carries_each_variants_own_price_and_marks_the_main_one()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var (goodId, mainId, otherId) = await WithUnitOfWorkAsync(async () =>
            {
                var code = "LOOK-" + Guid.NewGuid().ToString("N")[..8];
                var good = await _goodRepository.InsertAsync(new Good(code, $"Mamul {code}", companyId), autoSave: true);
                var main = await InsertPricedVariantAsync(companyId, good.Id, "ANA", isMain: true, price: 1000m);
                var other = await InsertPricedVariantAsync(companyId, good.Id, "DIGER", isMain: false, price: 2500m);
                return (good.Id, main, other);
            });

            var rows = (await WithUnitOfWorkAsync(async () => await _goodAppService.GetVariantLookupAsync()))
                .Where(r => r.CommodityId == goodId)
                .ToList();

            rows.Count.ShouldBe(2);
            rows.Single(r => r.VariantId == mainId).IsMain.ShouldBeTrue();
            rows.Single(r => r.VariantId == mainId).EntryPrice.ShouldBe(1000m);
            rows.Single(r => r.VariantId == otherId).EntryPrice.ShouldBe(2500m);   // KENDİ fiyatı — ana varyanta çökmez
            rows.First().IsMain.ShouldBeTrue();                                     // ana varyant önce (combo ilk seçim)
            rows.ShouldAllBe(r => r.DisplayText.StartsWith(r.CommodityCode + " / "));
        }
    }

    [Fact]
    public async Task Jewelry_variant_lookup_shares_the_jewelry_price_across_variants()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var jewelryId = await WithUnitOfWorkAsync(async () =>
            {
                var code = "MUC-" + Guid.NewGuid().ToString("N")[..8];
                var jewelry = new Jewelry(code, $"Mucevher {code}", companyId);
                jewelry.SetPricing(isQuantity: true, priceByQuantity: true, priceTypeChange: false,
                    entryPrice: 700m, entryPriceUnitId: null, exitPrice: 900m, exitPriceUnitId: null);
                await _jewelryRepository.InsertAsync(jewelry, autoSave: true);
                await _variantRepository.InsertAsync(new EntityVariant(companyId, "Jewelry", jewelry.Id, "A22", "22 Ayar", isMain: true), autoSave: true);
                await _variantRepository.InsertAsync(new EntityVariant(companyId, "Jewelry", jewelry.Id, "A14", "14 Ayar", isMain: false), autoSave: true);
                return jewelry.Id;
            });

            var rows = (await WithUnitOfWorkAsync(async () => await _jewelryAppService.GetVariantLookupAsync()))
                .Where(r => r.CommodityId == jewelryId)
                .ToList();

            rows.Count.ShouldBe(2);
            rows.ShouldAllBe(r => r.EntryPrice == 700m);   // varyantlar fiyatı PAYLAŞIR — bilinçli kısıt
            rows.Count(r => r.IsMain).ShouldBe(1);
        }
    }

    private async Task<Guid> InsertPricedVariantAsync(Guid companyId, Guid goodId, string code, bool isMain, decimal price)
    {
        var variant = await _variantRepository.InsertAsync(
            new EntityVariant(companyId, "Good", goodId, code, $"Varyant {code}", isMain), autoSave: true);
        var detail = new GoodVariantDetail(companyId, variant.Id);
        detail.SetPurchasePrice(price, entryPriceUnitId: null, taxIncluded: false);
        await _detailRepository.InsertAsync(detail, autoSave: true);
        return variant.Id;
    }
}
