using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil M4 köprüsü uçtan uca — <c>ApplySubstitutionAsync</c>: gerçek grup + maden kataloğu + voucher-beslemeli
/// stok üzerinden Top-N kombinasyonun kanal "Kombinasyon" özelliği + StockItem'lara (reçete + paket stoğu)
/// dönüştüğü, yeniden uygulamanın imza-bazlı RECONCILE olduğu (id/override korunur, fazlası silinir) ve
/// kullanıcının elle eklediği DİĞER özelliğin bozulmadığı pinlenir. Trendyol adaptörü aynı nötr planın
/// İKİNCİ tüketicisi olarak ayrıca doğrulanır. Konsept 12gr örneğinin 10/5/1 gr evreni kullanılır
/// (kur 1/1 seed'li — TRY ülkesi bağlanır, hesap fail-fast'i geçer; işçilik yok → tüm 12gr kombinasyonları
/// eşit maliyette → sıralama parça sayısına düşer).
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class SubstitutionChannelBridgeTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ISalesChannelTrN11ProductAppService _n11AppService;
    private readonly ISalesChannelTrTrendyolProductAppService _trendyolAppService;
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IRepository<SubstitutionGroup, Guid> _groupRepository;
    private readonly IRepository<SubstitutionGroupItem, Guid> _itemRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<SalesChannelTrN11, Guid> _n11ChannelRepository;
    private readonly IRepository<SalesChannelTrN11ProductAttribute, Guid> _n11AttributeRepository;
    private readonly IRepository<SalesChannelTrN11ProductAttributeValue, Guid> _n11AttributeValueRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItem, Guid> _n11StockItemRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> _n11RecipeLineRepository;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _trendyolChannelRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductAttribute, Guid> _trendyolAttributeRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductAttributeValue, Guid> _trendyolAttributeValueRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItem, Guid> _trendyolStockItemRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid> _trendyolRecipeLineRepository;

    public SubstitutionChannelBridgeTests()
    {
        _n11AppService                    = GetRequiredService<ISalesChannelTrN11ProductAppService>();
        _trendyolAppService               = GetRequiredService<ISalesChannelTrTrendyolProductAppService>();
        _voucherAppService                = GetRequiredService<IVoucherAppService>();
        _seeder                           = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext                   = GetRequiredService<TestCompanyContextProvider>();
        _metalRepository                  = GetRequiredService<IRepository<Metal, Guid>>();
        _groupRepository                  = GetRequiredService<IRepository<SubstitutionGroup, Guid>>();
        _itemRepository                   = GetRequiredService<IRepository<SubstitutionGroupItem, Guid>>();
        _productRepository                = GetRequiredService<IRepository<Product, Guid>>();
        _n11ChannelRepository             = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _n11AttributeRepository           = GetRequiredService<IRepository<SalesChannelTrN11ProductAttribute, Guid>>();
        _n11AttributeValueRepository      = GetRequiredService<IRepository<SalesChannelTrN11ProductAttributeValue, Guid>>();
        _n11StockItemRepository           = GetRequiredService<IRepository<SalesChannelTrN11ProductStockItem, Guid>>();
        _n11RecipeLineRepository          = GetRequiredService<IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid>>();
        _trendyolChannelRepository        = GetRequiredService<IRepository<SalesChannelTrTrendyol, Guid>>();
        _trendyolAttributeRepository      = GetRequiredService<IRepository<SalesChannelTrTrendyolProductAttribute, Guid>>();
        _trendyolAttributeValueRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProductAttributeValue, Guid>>();
        _trendyolStockItemRepository      = GetRequiredService<IRepository<SalesChannelTrTrendyolProductStockItem, Guid>>();
        _trendyolRecipeLineRepository     = GetRequiredService<IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid>>();
    }

    // ── N11: ilk uygulama → özellik + değerler + StockItem reçete/paket ─────────────────────────────

    [Fact]
    public async Task Apply_on_n11_creates_combination_attribute_values_stock_items_recipes_and_package_stock()
    {
        var scenario = await SeedScenarioAsync("MB1");
        var created = await CreateN11ProductAsync(scenario);

        var result = await _n11AppService.ApplySubstitutionAsync(created.Id, ApplyInput(scenario, topN: 3));

        // Plan özeti: kurlar 1/1 + işçilik yok → 12gr kombinasyonları eşit maliyette → sıra parça sayısına düşer (3 < 4 < 8 parça).
        result.ToleranceNotice.ShouldBeNull();   // grup toleransı 0
        result.Items.Select(i => (i.Rank, i.ValueText, i.PackageCount, i.StockItemCount)).ShouldBe(new[]
        {
            (1, "1×10gr + 2×1gr", 3, 1),
            (2, "2×5gr + 2×1gr", 3, 1),
            (3, "1×5gr + 7×1gr", 2, 1),
        });
        result.Items[0].IsPrimary.ShouldBeTrue();
        result.Items.Skip(1).ShouldAllBe(i => !i.IsPrimary);

        // Özellik grafı: tek "Kombinasyon" özelliği + Rank sıralı (DisplayOrder 0..2) 3 değer.
        var attribute = (await WithUnitOfWorkAsync(() =>
                _n11AttributeRepository.GetListAsync(a => a.SalesChannelTrN11ProductId == created.Id)))
            .ShouldHaveSingleItem();
        attribute.Name.ShouldBe(SubstitutionBridgeConsts.CombinationAttributeName);
        attribute.Id.ShouldBe(result.CombinationAttributeId);
        var values = await GetN11ValuesAsync(attribute.Id);
        values.Select(v => v.DisplayOrder).ShouldBe(new[] { 0, 1, 2 });

        // StockItem'lar: kombinasyon-başına 1 satır; kanal-only (ERP eşleşmesi yok) + paket stoğu + reçete.
        var headers = await GetN11HeadersAsync(created.Id);
        headers.Count.ShouldBe(3);
        headers.ShouldAllBe(h => h.ProductVariantId == null && h.OverridePrice == null && h.Margin == null);

        var bestHeader = HeaderOfValue(headers, attribute.Id, values[0].Id);
        bestHeader.OverrideStock.ShouldBe(3);

        // Rank1 reçetesi: 1×10gr (Amount 10) + 2×1gr (Amount 2) — metal satırları, Normal ödeme;
        // fiyat ELLE YAZILMADI (OverridePrice null) → türetilmiş fiyat mevcut maliyet zincirinden doğar.
        var bestRecipe = await GetN11RecipeLinesAsync(created.Id, bestHeader.Id);
        bestRecipe.Select(r => (r.CommodityId, r.Quantity, r.Amount, r.PaymentType)).ShouldBe(new[]
        {
            ((Guid?)scenario.Ten.Id, 1m, 10m, ProcessPaymentType.Normal),
            ((Guid?)scenario.One.Id, 2m, 2m, ProcessPaymentType.Normal),
        });
        bestRecipe.ShouldAllBe(r =>
            r.ComponentType == RecipeComponentType.CatalogCommodity && r.CommodityProcessType == ProcessType.Metal);
    }

    // ── N11: yeniden uygulama = reconcile (koru/güncelle/fazlayı sil) ───────────────────────────────

    [Fact]
    public async Task Reapply_preserves_matching_stock_items_with_user_overrides_and_removes_the_surplus()
    {
        var scenario = await SeedScenarioAsync("MB2");
        var created = await CreateN11ProductAsync(scenario);

        var first = await _n11AppService.ApplySubstitutionAsync(created.Id, ApplyInput(scenario, topN: 3));
        var attributeId = first.CombinationAttributeId;
        var valuesBefore = await GetN11ValuesAsync(attributeId);
        var headersBefore = await GetN11HeadersAsync(created.Id);
        headersBefore.Count.ShouldBe(3);

        // Kullanıcı emeği: Rank1 satırına fiyat + marj override'ı (köprü bunlara DOKUNMAZ).
        var bestHeader = HeaderOfValue(headersBefore, attributeId, valuesBefore[0].Id);
        await WithUnitOfWorkAsync(async () =>
        {
            var header = await _n11StockItemRepository.GetAsync(bestHeader.Id);
            header.SetOverridePrice(150m, null);
            header.SetMargin(20m);
            await _n11StockItemRepository.UpdateAsync(header, autoSave: true);
        });

        // İkinci uygulama TopN=2 → ilk iki kombinasyon imza-bazlı KORUNUR, üçüncüsü (değer + satır + reçete) silinir.
        var second = await _n11AppService.ApplySubstitutionAsync(created.Id, ApplyInput(scenario, topN: 2));
        second.CombinationAttributeId.ShouldBe(attributeId);   // özellik yeniden yaratılmadı
        second.Items.Count.ShouldBe(2);

        var valuesAfter = await GetN11ValuesAsync(attributeId);
        valuesAfter.Select(v => v.Id).ShouldBe(valuesBefore.Take(2).Select(v => v.Id));   // değer id'leri korundu

        var headersAfter = await GetN11HeadersAsync(created.Id);
        headersAfter.Count.ShouldBe(2);

        // Korunan Rank1 satırı: AYNI id + kullanıcı override'ları yaşıyor; paket stoğu köprüce tazelendi.
        var preserved = headersAfter.Single(h => h.Id == bestHeader.Id);
        preserved.OverridePrice.ShouldBe(150m);
        preserved.Margin.ShouldBe(20m);
        preserved.OverrideStock.ShouldBe(3);

        // Silinen üçüncü satırın reçetesi de gitti (cascade — reconcile removeAsync).
        var thirdHeader = HeaderOfValue(headersBefore, attributeId, valuesBefore[2].Id);
        (await GetN11RecipeLinesAsync(created.Id, thirdHeader.Id)).ShouldBeEmpty();
    }

    // ── N11: kullanıcının elle eklediği DİĞER özellik bozulmaz ──────────────────────────────────────

    [Fact]
    public async Task Apply_leaves_manually_added_attribute_intact_and_expands_cartesian_with_combination_axis()
    {
        var scenario = await SeedScenarioAsync("MB3");
        var created = await CreateN11ProductAsync(scenario, new SalesChannelTrN11ProductAttributeDto
        {
            Name = "Renk",
            DisplayOrder = 0,
            Values = new List<SalesChannelTrN11ProductAttributeValueDto>
            {
                new() { Value = "Kirmizi", DisplayOrder = 0 },
            },
        });

        var result = await _n11AppService.ApplySubstitutionAsync(created.Id, ApplyInput(scenario, topN: 2));

        // Renk özelliği + değeri el değmeden duruyor; Kombinasyon İKİNCİ eksen olarak eklendi.
        var attributes = await WithUnitOfWorkAsync(() =>
            _n11AttributeRepository.GetListAsync(a => a.SalesChannelTrN11ProductId == created.Id));
        attributes.Count.ShouldBe(2);
        var renk = attributes.Single(a => a.Name == "Renk");
        (await GetN11ValuesAsync(renk.Id)).ShouldHaveSingleItem().Value.ShouldBe("Kirmizi");

        // Kartezyen: 1 Renk × 2 Kombinasyon = 2 StockItem; her kombinasyon değeri 1'er satır taşıyor
        // ve köprü her satıra reçete + paket stoğu yazdı.
        result.Items.ShouldAllBe(i => i.StockItemCount == 1);
        var headers = await GetN11HeadersAsync(created.Id);
        headers.Count.ShouldBe(2);
        foreach (var header in headers)
        {
            header.CombinationSignature!.Split('|').Length.ShouldBe(2);   // Renk çifti + Kombinasyon çifti
            header.OverrideStock.ShouldNotBeNull();
            (await GetN11RecipeLinesAsync(created.Id, header.Id)).ShouldNotBeEmpty();
        }
    }

    // ── Tolerans ticari bildirimi + guard'lar ───────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_returns_tolerance_notice_for_permille_group_and_fails_fast_on_guards()
    {
        var scenario = await SeedScenarioAsync("MB4", toleranceType: ToleranceType.PerMille, toleranceValue: 1m);
        var created = await CreateN11ProductAsync(scenario);

        // Tolerans > 0 → push açıklamasına iliştirilecek ticari metin köprü sonucunda döner (üretim bu dilimde;
        // açıklamaya ekleme ayrı dilim).
        var result = await _n11AppService.ApplySubstitutionAsync(created.Id, ApplyInput(scenario, topN: 1));
        result.ToleranceNotice.ShouldBe("+/− binde 1 tolerans hakkı saklıdır");

        // Ulaşılamaz talep → yalnız başarısız denemeler → NoSuccessfulCombination (varyant kurulmaz).
        var unreachable = ApplyInput(scenario, topN: 1);
        unreachable.TargetQuantity = 10_000m;
        (await Should.ThrowAsync<BusinessException>(() =>
                _n11AppService.ApplySubstitutionAsync(created.Id, unreachable)))
            .Code.ShouldBe("TradeXpress:Substitution:NoSuccessfulCombination");

        // Varyant sayısı kullanıcı seçimi — pozitif olmalı.
        var invalidTopN = ApplyInput(scenario, topN: 0);
        (await Should.ThrowAsync<BusinessException>(() =>
                _n11AppService.ApplySubstitutionAsync(created.Id, invalidTopN)))
            .Code.ShouldBe("TradeXpress:Substitution:TopNInvalid");
    }

    // ── Trendyol adaptörü — aynı nötr planın İKİNCİ tüketicisi ──────────────────────────────────────

    [Fact]
    public async Task Apply_on_trendyol_consumes_the_same_neutral_plan_through_its_own_graph_types()
    {
        var scenario = await SeedScenarioAsync("MB5");
        var trendyolCreated = await CreateTrendyolProductAsync(scenario);

        var result = await _trendyolAppService.ApplySubstitutionAsync(trendyolCreated.Id, ApplyInput(scenario, topN: 2));

        // Plan metinleri N11 ile BİREBİR aynı (tek planlayıcı) — uygulama Trendyol graf tiplerinde.
        result.Items.Select(i => (i.Rank, i.ValueText, i.PackageCount)).ShouldBe(new[]
        {
            (1, "1×10gr + 2×1gr", 3),
            (2, "2×5gr + 2×1gr", 3),
        });

        var attribute = (await WithUnitOfWorkAsync(() =>
                _trendyolAttributeRepository.GetListAsync(a => a.SalesChannelTrTrendyolProductId == trendyolCreated.Id)))
            .ShouldHaveSingleItem();
        attribute.Name.ShouldBe(SubstitutionBridgeConsts.CombinationAttributeName);
        (await WithUnitOfWorkAsync(() =>
                _trendyolAttributeValueRepository.GetListAsync(v => v.AttributeId == attribute.Id)))
            .Count.ShouldBe(2);

        var headers = await WithUnitOfWorkAsync(() =>
            _trendyolStockItemRepository.GetListAsync(h =>
                h.SalesChannelTrTrendyolProductId == trendyolCreated.Id && h.CombinationSignature != null));
        headers.Count.ShouldBe(2);
        headers.ShouldAllBe(h => h.OverrideStock == 3);   // iki kombinasyonun paketi de 3

        foreach (var header in headers)
        {
            var recipe = await WithUnitOfWorkAsync(() =>
                _trendyolRecipeLineRepository.GetListAsync(r =>
                    r.SalesChannelTrTrendyolProductId == trendyolCreated.Id && r.StockItemId == header.Id));
            recipe.Count.ShouldBe(2);   // her kombinasyon 2 metal satırı (10+1 ya da 5+1)
        }
    }

    // ── senaryo/seed yardımcıları ───────────────────────────────────────────────────────────────────

    private sealed record BridgeScenario(
        VoucherTestData Data,
        Metal Ten,
        Metal Five,
        Metal One,
        Guid GroupId,
        string Prefix);

    /// <summary>Konsept 12gr evreninin 10/5/1 gr kesiti: stok 3×10, 7×5, 20×1; grup tüketim önceliği 10→5→1.</summary>
    private async Task<BridgeScenario> SeedScenarioAsync(
        string prefix, ToleranceType toleranceType = ToleranceType.Amount, decimal toleranceValue = 0m)
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync(prefix));
        _companyContext.CompanyId = data.CompanyId;

        // Kur seed'i: yerel para çözülebilen ülke (TRY) → hesap RatesMissing fail-fast'ini geçer (2026-07-10 kararı).
        await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, prefix));

        var ten  = await SeedMetalAsync(data, $"{prefix}TEN", 10m);
        var five = await SeedMetalAsync(data, $"{prefix}FIVE", 5m);
        var one  = await SeedMetalAsync(data, $"{prefix}ONE", 1m);

        await SeedInboundStockAsync(data, ten, count: 3);
        await SeedInboundStockAsync(data, five, count: 7);
        await SeedInboundStockAsync(data, one, count: 20);

        var groupId = await WithUnitOfWorkAsync(async () =>
        {
            var group = new SubstitutionGroup(data.CompanyId, $"{prefix}GRP", $"{prefix} Group");
            group.SetTolerance(toleranceType, toleranceValue);
            await _groupRepository.InsertAsync(group, autoSave: true);
            var metalIds = new[] { ten.Id, five.Id, one.Id };
            for (var order = 0; order < metalIds.Length; order++)
            {
                await _itemRepository.InsertAsync(
                    new SubstitutionGroupItem(data.CompanyId, group.Id, metalIds[order], displayOrder: order),
                    autoSave: true);
            }

            return group.Id;
        });

        return new BridgeScenario(data, ten, five, one, groupId, prefix);
    }

    private static SubstitutionApplyInput ApplyInput(BridgeScenario scenario, int topN)
    {
        return new SubstitutionApplyInput
        {
            SubstitutionGroupId = scenario.GroupId,
            TargetQuantity      = 12m,
            TopN                = topN,
            BranchId            = scenario.Data.BranchId,
        };
    }

    private async Task<SalesChannelTrN11ProductDto> CreateN11ProductAsync(
        BridgeScenario scenario, params SalesChannelTrN11ProductAttributeDto[] channelAttributes)
    {
        var (channelId, productId) = await WithUnitOfWorkAsync(async () =>
        {
            var channel = await _n11ChannelRepository.InsertAsync(
                new SalesChannelTrN11(
                    scenario.Data.CompanyId, $"N11-{scenario.Prefix}", $"N11 Kanal {scenario.Prefix}", "app-key", "app-secret"),
                autoSave: true);
            var product = await _productRepository.InsertAsync(
                new Product(scenario.Data.CompanyId, $"{scenario.Prefix}PROD", $"Urun {scenario.Prefix}"), autoSave: true);
            return (channel.Id, product.Id);
        });

        return await _n11AppService.CreateAsync(new SalesChannelTrN11ProductCreateDto
        {
            ProductId = productId,
            SalesChannelId = channelId,
            CategoryExternalId = "1000846",
            ShipmentTemplateName = "Standart Teslimat",
            ProductAttributes = channelAttributes.ToList(),
        });
    }

    private async Task<SalesChannelTrTrendyolProductDto> CreateTrendyolProductAsync(BridgeScenario scenario)
    {
        var (channelId, productId) = await WithUnitOfWorkAsync(async () =>
        {
            var channel = await _trendyolChannelRepository.InsertAsync(
                new SalesChannelTrTrendyol(
                    scenario.Data.CompanyId, $"TY-{scenario.Prefix}", $"Trendyol Kanal {scenario.Prefix}",
                    "seller-1", "api-key", "api-secret"),
                autoSave: true);
            var product = await _productRepository.InsertAsync(
                new Product(scenario.Data.CompanyId, $"{scenario.Prefix}TYPROD", $"Urun TY {scenario.Prefix}"), autoSave: true);
            return (channel.Id, product.Id);
        });

        return await _trendyolAppService.CreateAsync(new SalesChannelTrTrendyolProductCreateDto
        {
            ProductId = productId,
            SalesChannelId = channelId,
            CategoryId = "411",
            BrandId = "1",
        });
    }

    /// <summary>Adet-hesaplı + standart gramajlı maden kataloğu kaydı (HAS takipli, milyem 1).</summary>
    private Task<Metal> SeedMetalAsync(VoucherTestData data, string code, decimal pieceWeight)
    {
        return WithUnitOfWorkAsync(() => _metalRepository.InsertAsync(
            new Metal(code, $"{code} Metal", data.HasUnitId, factor: 1m,
                isQuantity: true, stableQuantity: pieceWeight),
            autoSave: true));
    }

    /// <summary>Fiziksel stok girişi: Normal Giriş, adet × parça gramı (stok raporu AvailableQuantity beslemesi).</summary>
    private Task SeedInboundStockAsync(VoucherTestData data, Metal metal, int count)
    {
        var amount = count * metal.StableQuantity;
        return _voucherAppService.SaveLineAsync(new VoucherLineDto
        {
            BranchId      = data.BranchId,
            VaultId       = data.VaultId,
            AccountId     = data.AccountId,
            SubAccountId  = data.SubAccountId,
            Type          = ProcessType.Metal,
            Direction     = ProcessDirectionType.Inbound,
            PaymentType   = ProcessPaymentType.Normal,
            CommodityId   = metal.Id,
            CommodityCode = metal.Code,
            Quantity      = count,
            Amount        = amount,
            Factor        = 1m,
            Total         = amount,
            MainUnitId    = data.HasUnitId,
        });
    }

    // ── okuma yardımcıları ──────────────────────────────────────────────────────────────────────────

    private async Task<List<SalesChannelTrN11ProductAttributeValue>> GetN11ValuesAsync(Guid attributeId)
    {
        var values = await WithUnitOfWorkAsync(() =>
            _n11AttributeValueRepository.GetListAsync(v => v.AttributeId == attributeId));
        return values.OrderBy(v => v.DisplayOrder).ToList();
    }

    private async Task<List<SalesChannelTrN11ProductStockItem>> GetN11HeadersAsync(Guid channelProductId)
    {
        return await WithUnitOfWorkAsync(() =>
            _n11StockItemRepository.GetListAsync(h =>
                h.SalesChannelTrN11ProductId == channelProductId && h.CombinationSignature != null));
    }

    private async Task<List<SalesChannelTrN11ProductStockItemRecipeLine>> GetN11RecipeLinesAsync(
        Guid channelProductId, Guid stockItemId)
    {
        var lines = await WithUnitOfWorkAsync(() =>
            _n11RecipeLineRepository.GetListAsync(r =>
                r.SalesChannelTrN11ProductId == channelProductId && r.StockItemId == stockItemId));
        return lines.OrderBy(l => l.LineOrder).ToList();
    }

    /// <summary>Belirli kombinasyon DEĞERİNİ imzasında taşıyan tek StockItem satırı.</summary>
    private static SalesChannelTrN11ProductStockItem HeaderOfValue(
        List<SalesChannelTrN11ProductStockItem> headers, Guid attributeId, Guid valueId)
    {
        var token = $"{attributeId}={valueId}";
        return headers.Single(h =>
            h.CombinationSignature!.Split('|', StringSplitOptions.RemoveEmptyEntries).Contains(token));
    }
}
