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
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// M4 köprüsünün VARYANT boyutu (Dilim-2 + A6) uçtan uca — <c>ApplySubstitutionAsync</c>:
/// çözücünün SEÇTİĞİ metal varyantı kanal StockItem reçetesine <c>CommodityVariantId</c> olarak persist edilir
/// ve işçilik bacağı (PayFactor) SEÇİLEN varyantın MetalVariantDetail'inden gelir (ana-varyant değil).
/// Kanal apply zinciri: plan → BuildRecipeLineDtos → ApplyChannelRecipeLineFields → SetCatalogCommodity(A6 kolonu).
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class SubstitutionChannelVariantBridgeTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ISalesChannelTrN11ProductAppService _n11AppService;
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IRepository<SubstitutionGroup, Guid> _groupRepository;
    private readonly IRepository<SubstitutionGroupItem, Guid> _itemRepository;
    private readonly IRepository<EntityVariant, Guid> _entityVariantRepository;
    private readonly IRepository<MetalVariantDetail, Guid> _metalVariantDetailRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<SalesChannelTrN11, Guid> _n11ChannelRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItem, Guid> _n11StockItemRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> _n11RecipeLineRepository;

    public SubstitutionChannelVariantBridgeTests()
    {
        _n11AppService                = GetRequiredService<ISalesChannelTrN11ProductAppService>();
        _voucherAppService            = GetRequiredService<IVoucherAppService>();
        _seeder                       = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext               = GetRequiredService<TestCompanyContextProvider>();
        _metalRepository              = GetRequiredService<IRepository<Metal, Guid>>();
        _groupRepository              = GetRequiredService<IRepository<SubstitutionGroup, Guid>>();
        _itemRepository               = GetRequiredService<IRepository<SubstitutionGroupItem, Guid>>();
        _entityVariantRepository      = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _metalVariantDetailRepository = GetRequiredService<IRepository<MetalVariantDetail, Guid>>();
        _productRepository            = GetRequiredService<IRepository<Product, Guid>>();
        _n11ChannelRepository         = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _n11StockItemRepository       = GetRequiredService<IRepository<SalesChannelTrN11ProductStockItem, Guid>>();
        _n11RecipeLineRepository      = GetRequiredService<IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid>>();
    }

    [Fact]
    public async Task Apply_persists_selected_variant_id_and_variant_labor_on_channel_recipe_lines()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("MVB"));
        _companyContext.CompanyId = data.CompanyId;
        await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, "MVB"));

        // Katalog: 10gr (yalnız ana varyant, işçilik 0) + 1gr (ana 2 TRY/adet, ESKI 1 TRY/adet işçilik).
        var ten = await SeedMetalAsync(data, "MVBTEN", 10m);
        var tenMainId = await SeedVariantAsync(ten, "MAIN", isMain: true, laborPerPiece: 0m, data.TryUnitId);
        var one = await SeedMetalAsync(data, "MVBONE", 1m);
        var oneMainId = await SeedVariantAsync(one, "MAIN", isMain: true, laborPerPiece: 2m, data.TryUnitId);
        var oneAltId  = await SeedVariantAsync(one, "ESKI", isMain: false, laborPerPiece: 1m, data.TryUnitId);

        await SeedInboundStockAsync(data, ten, count: 3, tenMainId, "MVBTEN-MAIN");
        await SeedInboundStockAsync(data, one, count: 20, oneMainId, "MVBONE-MAIN");
        await SeedInboundStockAsync(data, one, count: 20, oneAltId, "MVBONE-ESKI");

        // Grup: 10gr ana-yalnız (boş küme = statüko), 1gr her iki varyant dahil.
        var groupId = await WithUnitOfWorkAsync(async () =>
        {
            var group = await _groupRepository.InsertAsync(
                new SubstitutionGroup(data.CompanyId, "MVBGRP", "MVB Group"), autoSave: true);
            await _itemRepository.InsertAsync(
                new SubstitutionGroupItem(data.CompanyId, group.Id, ten.Id, displayOrder: 0), autoSave: true);
            var oneItem = new SubstitutionGroupItem(data.CompanyId, group.Id, one.Id, displayOrder: 1);
            oneItem.SetIncludedVariants(new[] { oneMainId, oneAltId });
            await _itemRepository.InsertAsync(oneItem, autoSave: true);
            return group.Id;
        });

        var created = await CreateN11ProductAsync(data);

        // Rank-1 = 1×10 + 2×1(ESKI): 10 + 2×(1+1) = 14 TRY — ana 1gr'lı muadili (16 TRY) ucuz varyant geçer.
        var result = await _n11AppService.ApplySubstitutionAsync(created.Id, new SubstitutionApplyInput
        {
            SubstitutionGroupId = groupId,
            TargetQuantity      = 12m,
            TopN                = 1,
            BranchId            = data.BranchId,
        });

        var applied = result.Items.ShouldHaveSingleItem();
        applied.Rank.ShouldBe(1);
        applied.ValueText.ShouldBe("1×10gr + 2×1gr");
        applied.PackageCount.ShouldBe(3);   // min(3/1, 20/2)

        // Kanal reçetesi SEÇİLEN varyantı persist etti (A6 kolonu) + işçilik SEÇİLEN varyantın detayından:
        // 1gr satırının PayFactor'ı ESKI'nin 1 TRY'si (ana varyantın 2 TRY'si DEĞİL).
        var header = (await WithUnitOfWorkAsync(() =>
                _n11StockItemRepository.GetListAsync(h =>
                    h.SalesChannelTrN11ProductId == created.Id && h.CombinationSignature != null)))
            .ShouldHaveSingleItem();
        var recipe = (await WithUnitOfWorkAsync(() =>
                _n11RecipeLineRepository.GetListAsync(r =>
                    r.SalesChannelTrN11ProductId == created.Id && r.StockItemId == header.Id)))
            .OrderBy(r => r.LineOrder)
            .ToList();

        recipe.Select(r => (r.CommodityId, r.CommodityVariantId, r.Quantity, r.Amount, r.PayFactor)).ShouldBe(new[]
        {
            ((Guid?)ten.Id, (Guid?)tenMainId, 1m, 10m, 0m),
            ((Guid?)one.Id, (Guid?)oneAltId, 2m, 2m, 1m),
        });
    }

    // ── seed yardımcıları ───────────────────────────────────────────────────────────────────────────

    private Task<Metal> SeedMetalAsync(VoucherTestData data, string code, decimal pieceWeight)
    {
        return WithUnitOfWorkAsync(() => _metalRepository.InsertAsync(
            new Metal(code, $"{code} Metal", data.HasUnitId, companyId: data.CompanyId, factor: 1m,
                isQuantity: true, stableQuantity: pieceWeight),
            autoSave: true));
    }

    private Task<Guid> SeedVariantAsync(Metal metal, string suffix, bool isMain, decimal laborPerPiece, Guid? laborUnitId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var variant = await _entityVariantRepository.InsertAsync(
                new EntityVariant(
                    companyId: null, entityName: "Metal", entityId: metal.Id,
                    code: $"{metal.Code}-{suffix}", name: $"{metal.Name} {suffix}", isMain: isMain),
                autoSave: true);

            var detail = new MetalVariantDetail(companyId: null, entityVariantId: variant.Id);
            detail.SetLabor(
                MetalLaborType.Quantity, laborTypeChange: false,
                entryLabor: laborPerPiece, entryLaborUnitId: laborUnitId, entryLaborChange: false,
                exitLabor: 0m, exitLaborUnitId: null, exitLaborChange: false,
                costUnitId: null);
            await _metalVariantDetailRepository.InsertAsync(detail, autoSave: true);

            return variant.Id;
        });
    }

    private Task SeedInboundStockAsync(VoucherTestData data, Metal metal, int count, Guid? variantId, string? variantCode)
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
            VariantId     = variantId,
            VariantCode   = variantCode,
            Quantity      = count,
            Amount        = amount,
            Factor        = 1m,
            Total         = amount,
            MainUnitId    = data.HasUnitId,
        });
    }

    private async Task<SalesChannelTrN11ProductDto> CreateN11ProductAsync(VoucherTestData data)
    {
        var (channelId, productId) = await WithUnitOfWorkAsync(async () =>
        {
            var channel = await _n11ChannelRepository.InsertAsync(
                new SalesChannelTrN11(
                    data.CompanyId, "N11-MVB", "N11 Kanal MVB", "app-key", "app-secret"),
                autoSave: true);
            var product = await _productRepository.InsertAsync(
                new Product(data.CompanyId, "MVBPROD", "Urun MVB"), autoSave: true);
            return (channel.Id, product.Id);
        });

        return await _n11AppService.CreateAsync(new SalesChannelTrN11ProductCreateDto
        {
            ProductId = productId,
            SalesChannelId = channelId,
            CategoryExternalId = "1000846",
            ShipmentTemplateName = "Standart Teslimat",
            ProductAttributes = new List<SalesChannelTrN11ProductAttributeDto>(),
        });
    }
}
