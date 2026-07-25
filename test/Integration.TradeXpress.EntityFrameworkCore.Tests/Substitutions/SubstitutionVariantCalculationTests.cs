using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil hesabının VARYANT-düzeyi değerlendirmesi (Dilim-2) uçtan uca:
/// <list type="bullet">
///   <item>Etkin küme (IncludedVariantIds) her varyantı AYRI aday-hat olarak açar — işçilik varyant-özel,
///   ucuz varyant Rank-1 olur.</item>
///   <item>Stok (MetalId, VariantId) kırılımlıdır — bir varyantın stoğu bitince kombinasyon diğerine düşer.</item>
///   <item>Dahil-listesi DIŞINDAKİ varyantın stoğu da işçiliği de değerlendirmeye GİRMEZ.</item>
///   <item>Varyantsız (legacy) hareket ANA varyant havuzuna normalize edilir (kesin karar).</item>
/// </list>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class SubstitutionVariantCalculationTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ISubstitutionCalculationAppService _calculationAppService;
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IRepository<SubstitutionGroup, Guid> _groupRepository;
    private readonly IRepository<SubstitutionGroupItem, Guid> _itemRepository;
    private readonly IRepository<EntityVariant, Guid> _entityVariantRepository;
    private readonly IRepository<MetalVariantDetail, Guid> _metalVariantDetailRepository;

    public SubstitutionVariantCalculationTests()
    {
        _calculationAppService        = GetRequiredService<ISubstitutionCalculationAppService>();
        _voucherAppService            = GetRequiredService<IVoucherAppService>();
        _seeder                       = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext               = GetRequiredService<TestCompanyContextProvider>();
        _metalRepository              = GetRequiredService<IRepository<Metal, Guid>>();
        _groupRepository              = GetRequiredService<IRepository<SubstitutionGroup, Guid>>();
        _itemRepository               = GetRequiredService<IRepository<SubstitutionGroupItem, Guid>>();
        _entityVariantRepository      = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _metalVariantDetailRepository = GetRequiredService<IRepository<MetalVariantDetail, Guid>>();
    }

    // ── (a) İki varyantlı maden, farklı işçilik → ucuz varyant Rank-1 ───────────────────────────────

    [Fact]
    public async Task Cheaper_variant_ranks_first_when_both_variants_are_included()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SVA"));
        _companyContext.CompanyId = data.CompanyId;
        await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, "SVA"));

        // 5gr maden, iki varyant: ana 3 TRY/adet işçilik, ESKI 1 TRY/adet → parça maliyeti 8 vs 6 TRY (kur 1/1).
        var metal = await SeedMetalAsync(data, "SVAFIVE", 5m);
        var mainVariantId = await SeedVariantAsync(metal, "MAIN", isMain: true, laborPerPiece: 3m, data.TryUnitId);
        var altVariantId  = await SeedVariantAsync(metal, "ESKI", isMain: false, laborPerPiece: 1m, data.TryUnitId);

        await SeedInboundStockAsync(data, metal, count: 10, mainVariantId, "SVAFIVE-MAIN");
        await SeedInboundStockAsync(data, metal, count: 10, altVariantId, "SVAFIVE-ESKI");

        var groupId = await SeedGroupAsync(data, "SVAGRP", (metal.Id, new[] { mainVariantId, altVariantId }));

        var result = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId = groupId,
            TargetQuantity      = 10m,
            BranchId            = data.BranchId,
        });

        // Aday uzayı varyantlaştı: 2×ana(16) · 1×ana+1×ESKI(14) · 2×ESKI(12) — ucuz varyant Rank-1.
        result.Trials.Where(t => t.Success)
            .OrderBy(t => t.Rank)
            .Select(t => t.TotalCost)
            .ShouldBe(new[] { 12m, 14m, 16m });

        var best = result.Trials.Single(t => t.Rank == 1);
        var bestLine = best.Lines.ShouldHaveSingleItem();
        bestLine.VariantId.ShouldBe(altVariantId);
        bestLine.VariantCode.ShouldBe("SVAFIVE-ESKI");
        bestLine.Count.ShouldBe(2);
        bestLine.UnitCost.ShouldBe(6m);

        // Karışık kombinasyon iki varyantı AYRI satır taşır (aynı maden, farklı varyant).
        var mixed = result.Trials.Single(t => t.Rank == 2);
        mixed.Lines.Select(l => (l.VariantId, l.Count)).ShouldBe(new[]
        {
            ((Guid?)mainVariantId, 1),
            ((Guid?)altVariantId, 1),
        });
    }

    // ── (b) Varyant-bazlı stok kısıtı: biri bitince diğerine düşer ──────────────────────────────────

    [Fact]
    public async Task Variant_stock_constraint_forces_fallback_to_the_other_variant()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SVB"));
        _companyContext.CompanyId = data.CompanyId;
        await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, "SVB"));

        // ESKI varyant ucuz ama stokta yalnız 1 adet → 10gr talep tek başına ESKI ile TUTMAZ.
        var metal = await SeedMetalAsync(data, "SVBFIVE", 5m);
        var mainVariantId = await SeedVariantAsync(metal, "MAIN", isMain: true, laborPerPiece: 3m, data.TryUnitId);
        var altVariantId  = await SeedVariantAsync(metal, "ESKI", isMain: false, laborPerPiece: 1m, data.TryUnitId);

        await SeedInboundStockAsync(data, metal, count: 10, mainVariantId, "SVBFIVE-MAIN");
        await SeedInboundStockAsync(data, metal, count: 1, altVariantId, "SVBFIVE-ESKI");

        var groupId = await SeedGroupAsync(data, "SVBGRP", (metal.Id, new[] { mainVariantId, altVariantId }));

        var result = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId = groupId,
            TargetQuantity      = 10m,
            BranchId            = data.BranchId,
        });

        // 2×ESKI stok yetmediği için HİÇ üretilemez; en iyi = 1×ana + 1×ESKI (14 TRY).
        result.Trials.ShouldAllBe(t =>
            t.Lines.Where(l => l.VariantId == altVariantId).Sum(l => l.Count) <= 1);
        var best = result.Trials.Single(t => t.Rank == 1);
        best.TotalCost.ShouldBe(14m);
        best.Lines.Select(l => (l.VariantId, l.Count)).ShouldBe(new[]
        {
            ((Guid?)mainVariantId, 1),
            ((Guid?)altVariantId, 1),
        });
    }

    // ── (c) Dahil-listesi dışındaki varyantın stok + işçiliği yok sayılır ───────────────────────────

    [Fact]
    public async Task Excluded_variant_stock_and_labor_are_ignored_entirely()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SVC"));
        _companyContext.CompanyId = data.CompanyId;
        await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, "SVC"));

        // Üç varyant: ana + ESKI dahil; UCUZ (işçilik 0, stok bol) kapsam DIŞI — sızarsa Rank-1 olurdu.
        var metal = await SeedMetalAsync(data, "SVCFIVE", 5m);
        var mainVariantId     = await SeedVariantAsync(metal, "MAIN", isMain: true, laborPerPiece: 3m, data.TryUnitId);
        var altVariantId      = await SeedVariantAsync(metal, "ESKI", isMain: false, laborPerPiece: 1m, data.TryUnitId);
        var excludedVariantId = await SeedVariantAsync(metal, "UCUZ", isMain: false, laborPerPiece: 0m, data.TryUnitId);

        await SeedInboundStockAsync(data, metal, count: 1, mainVariantId, "SVCFIVE-MAIN");
        await SeedInboundStockAsync(data, metal, count: 1, altVariantId, "SVCFIVE-ESKI");
        await SeedInboundStockAsync(data, metal, count: 10, excludedVariantId, "SVCFIVE-UCUZ");

        var groupId = await SeedGroupAsync(data, "SVCGRP", (metal.Id, new[] { mainVariantId, altVariantId }));

        var result = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId = groupId,
            TargetQuantity      = 10m,
            BranchId            = data.BranchId,
        });

        // Kapsam dışı varyant hiçbir denemede YOK; kapasite yalnız dahil adaylardan (1+1 parça = 10gr) —
        // UCUZ stoğu sızsaydı kapasite 60gr görünürdü.
        result.TotalAvailableWeight.ShouldBe(10m);
        result.Trials.SelectMany(t => t.Lines).ShouldAllBe(l => l.VariantId != excludedVariantId);

        var best = result.Trials.Single(t => t.Rank == 1);
        best.TotalCost.ShouldBe(14m);   // 8 (ana) + 6 (ESKI) — UCUZ'un 5 TRY'lik parçası hesaba girmedi
    }

    // ── (d) Varyantsız (legacy) hareket ANA varyant havuzuna akar ───────────────────────────────────

    [Fact]
    public async Task Variantless_legacy_stock_normalizes_into_the_main_variant_pool()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SVD"));
        _companyContext.CompanyId = data.CompanyId;
        await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, "SVD"));

        var metal = await SeedMetalAsync(data, "SVDFIVE", 5m);
        var mainVariantId = await SeedVariantAsync(metal, "MAIN", isMain: true, laborPerPiece: 3m, data.TryUnitId);
        var altVariantId  = await SeedVariantAsync(metal, "ESKI", isMain: false, laborPerPiece: 1m, data.TryUnitId);

        // Stok VARYANTSIZ girildi (legacy hareket) → ana havuza normalize edilir; ESKI stoksuz kalır.
        await SeedInboundStockAsync(data, metal, count: 4, variantId: null, variantCode: null);

        var groupId = await SeedGroupAsync(data, "SVDGRP", (metal.Id, new[] { mainVariantId, altVariantId }));

        var result = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId = groupId,
            TargetQuantity      = 10m,
            BranchId            = data.BranchId,
        });

        // ESKI aday stoksuz → ön-filtrede NoStock ile elendi (varyant koduyla raporlanır).
        var filtered = result.FilteredOut.ShouldHaveSingleItem();
        filtered.VariantId.ShouldBe(altVariantId);
        filtered.VariantCode.ShouldBe("SVDFIVE-ESKI");
        filtered.Reason.ShouldBe(SubstitutionReasonCodes.NoStock);

        // Legacy stok ANA adayda: 2×ana = 10gr ✓ (işçilik ana varyantın 3 TRY'siyle → 16 TRY).
        var best = result.Trials.Single(t => t.Rank == 1);
        var line = best.Lines.ShouldHaveSingleItem();
        line.VariantId.ShouldBe(mainVariantId);
        line.Count.ShouldBe(2);
        best.TotalCost.ShouldBe(16m);
    }

    // ── (e) Dilim-3: ürün-düzeyi override zinciri — override, grubun IncludedVariantIds'ını EZER ─────

    [Fact]
    public async Task Product_level_override_beats_group_included_list()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SVE"));
        _companyContext.CompanyId = data.CompanyId;
        await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, "SVE"));

        // Grup kapsamı yalnız ANA varyant (included=[main]); ürün override'ı ESKI'yi seçiyor → grup ayarı ezilmeli.
        var metal = await SeedMetalAsync(data, "SVEFIVE", 5m);
        var mainVariantId = await SeedVariantAsync(metal, "MAIN", isMain: true, laborPerPiece: 3m, data.TryUnitId);
        var altVariantId  = await SeedVariantAsync(metal, "ESKI", isMain: false, laborPerPiece: 1m, data.TryUnitId);

        await SeedInboundStockAsync(data, metal, count: 10, mainVariantId, "SVEFIVE-MAIN");
        await SeedInboundStockAsync(data, metal, count: 10, altVariantId, "SVEFIVE-ESKI");

        var groupId = await SeedGroupAsync(data, "SVEGRP", (metal.Id, new[] { mainVariantId }));

        // 1) Override YOK → grup ayarı: yalnız ana varyant değerlendirilir (statüko koruması).
        var withoutOverride = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId = groupId,
            TargetQuantity      = 10m,
            BranchId            = data.BranchId,
        });
        withoutOverride.Trials.SelectMany(t => t.Lines).ShouldAllBe(l => l.VariantId == mainVariantId);
        withoutOverride.Trials.Single(t => t.Rank == 1).TotalCost.ShouldBe(16m);   // 2×(5+3)

        // 2) Override=[ESKI] → grup listesi TAMAMEN ezilir: ana varyantın stoğu/işçiliği değerlendirmeye GİRMEZ.
        var withOverride = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId = groupId,
            TargetQuantity      = 10m,
            BranchId            = data.BranchId,
            OverrideVariantIds  = new List<Guid> { altVariantId },
        });

        withOverride.TotalAvailableWeight.ShouldBe(50m);   // yalnız ESKI stoğu (10×5gr); ana sızsaydı 100 görünürdü
        withOverride.Trials.SelectMany(t => t.Lines).ShouldAllBe(l => l.VariantId == altVariantId);
        var best = withOverride.Trials.Single(t => t.Rank == 1);
        best.TotalCost.ShouldBe(12m);   // 2×(5+1) — ucuz override varyantı
        best.Lines.ShouldHaveSingleItem().VariantCode.ShouldBe("SVEFIVE-ESKI");
    }

    // ── Dilim-3: ürün-düzeyi tolerans override'ı grup politikasını değiştirir ───────────────────────

    [Fact]
    public async Task Tolerance_override_replaces_group_policy_for_the_calculation()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SVF"));
        _companyContext.CompanyId = data.CompanyId;
        await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, "SVF"));

        // Grup toleransı 0 (mutlak eşitlik): 5gr parçalarla 12gr talebi TUTMAZ; override Gram 3 → 10gr/15gr tolerans içi.
        var metal = await SeedMetalAsync(data, "SVFFIVE", 5m);
        var mainVariantId = await SeedVariantAsync(metal, "MAIN", isMain: true, laborPerPiece: 1m, data.TryUnitId);
        await SeedInboundStockAsync(data, metal, count: 10, mainVariantId, "SVFFIVE-MAIN");

        var groupId = await SeedGroupAsync(data, "SVFGRP", (metal.Id, new[] { mainVariantId }));

        var exact = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId = groupId,
            TargetQuantity      = 12m,
            BranchId            = data.BranchId,
        });
        exact.SuccessCount.ShouldBe(0);   // grup politikası (mutlak eşitlik) — koruma

        var overridden = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId    = groupId,
            TargetQuantity         = 12m,
            BranchId               = data.BranchId,
            ToleranceTypeOverride  = ToleranceType.Amount,
            ToleranceValueOverride = 3m,
        });

        // Sonuç tablosu ETKİN politikayı gösterir (kullanılan tolerans = override).
        overridden.ToleranceType.ShouldBe(ToleranceType.Amount);
        overridden.ToleranceValue.ShouldBe(3m);
        overridden.EffectiveTolerance.ShouldBe(3m);
        overridden.SuccessCount.ShouldBeGreaterThan(0);   // 2×5=10gr (sapma -2) tolerans içi

        // Tutarsız çift (yalnız değer) → fail-fast (grup SetTolerance kuralıyla hizalı).
        var invalid = await Should.ThrowAsync<BusinessException>(() =>
            _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
            {
                SubstitutionGroupId    = groupId,
                TargetQuantity         = 12m,
                ToleranceValueOverride = 3m,
            }));
        invalid.Code.ShouldBe("TradeXpress:Substitution:ToleranceValueInvalid");
    }

    // ── (f) bayat dahil-varyant id budaması (kod-inceleme regresyonu) ───────────────────────────────

    /// <summary>Katalogda artık bulunmayan (bayat) dahil-varyant id'si hesabı KIRMAZ — override yoluyla simetrik
    /// budanır ve kalan geçerli varyantla hesap yürür. Regresyon koruması: kapsam artık her grup kalemi için somut
    /// id'lerle materyalize ediliyor ve varyantlar rutin olarak silinebiliyor (bir nitelik değeri kalkınca
    /// EntityVariantSynchronizer ilgili varyantı soft-delete eder) → eski fail-fast, sıradan bir katalog
    /// düzenlemesini o madeni içeren HER grubun hesabını (ürün formu, hesaplama sayfası, kanala-uygula)
    /// kilitleyen bir kesintiye çeviriyordu.</summary>
    [Fact]
    public async Task Stale_included_variant_id_is_pruned_instead_of_failing_the_calculation()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SVP"));
        _companyContext.CompanyId = data.CompanyId;
        await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, "SVP"));

        var metal = await SeedMetalAsync(data, "SVPFIVE", 5m);
        var mainVariantId = await SeedVariantAsync(metal, "MAIN", isMain: true, laborPerPiece: 1m, data.TryUnitId);
        await SeedInboundStockAsync(data, metal, count: 10, mainVariantId, "SVPFIVE-MAIN");

        // Kapsamda geçerli varyantın YANINDA katalogda olmayan bir id (silinmiş varyant senaryosu).
        var groupId = await SeedGroupAsync(data, "SVPGRP", (metal.Id, new[] { mainVariantId, Guid.NewGuid() }));

        var result = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId = groupId,
            TargetQuantity      = 10m,
            BranchId            = data.BranchId,
        });

        result.SuccessCount.ShouldBeGreaterThan(0);   // bayat id budandı, 2×5=10gr çözüm üretildi
    }

    /// <summary>Kapsamın TAMAMI bayatsa resolver ANA varyanta düşer (statüko) — hesap yine kırılmaz.</summary>
    [Fact]
    public async Task Fully_stale_included_variants_fall_back_to_main_variant()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SVQ"));
        _companyContext.CompanyId = data.CompanyId;
        await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, "SVQ"));

        var metal = await SeedMetalAsync(data, "SVQFIVE", 5m);
        var mainVariantId = await SeedVariantAsync(metal, "MAIN", isMain: true, laborPerPiece: 1m, data.TryUnitId);
        await SeedInboundStockAsync(data, metal, count: 10, mainVariantId, "SVQFIVE-MAIN");

        var groupId = await SeedGroupAsync(data, "SVQGRP", (metal.Id, new[] { Guid.NewGuid(), Guid.NewGuid() }));

        var result = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId = groupId,
            TargetQuantity      = 10m,
            BranchId            = data.BranchId,
        });

        result.SuccessCount.ShouldBeGreaterThan(0);
    }

    // ── seed yardımcıları ───────────────────────────────────────────────────────────────────────────

    /// <summary>Adet-hesaplı + standart gramajlı maden (HAS takipli, milyem 1) — varyantlar AYRI seed'lenir.</summary>
    private Task<Metal> SeedMetalAsync(VoucherTestData data, string code, decimal pieceWeight)
    {
        return WithUnitOfWorkAsync(() => _metalRepository.InsertAsync(
            new Metal(code, $"{code} Metal", data.HasUnitId, companyId: data.CompanyId, factor: 1m,
                isQuantity: true, stableQuantity: pieceWeight),
            autoSave: true));
    }

    /// <summary>Madene bir varyant + ADET-BAŞI işçilik detayı ekler (production maden-varyant deseni;
    /// varyant tenant-geneli: CompanyId=null). Varyant kodu "{MetalCode}-{suffix}".</summary>
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

    /// <summary>Muadil grubu + maden satırları; her satırın opt-in varyant kümesi parametreyle verilir
    /// (null/boş = yalnız ana varyant — statüko).</summary>
    private Task<Guid> SeedGroupAsync(VoucherTestData data, string code, params (Guid MetalId, Guid[]? IncludedVariantIds)[] items)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var group = await _groupRepository.InsertAsync(
                new SubstitutionGroup(data.CompanyId, code, $"{code} Group"), autoSave: true);
            for (var order = 0; order < items.Length; order++)
            {
                var item = new SubstitutionGroupItem(data.CompanyId, group.Id, items[order].MetalId, displayOrder: order);
                item.SetIncludedVariants(items[order].IncludedVariantIds);
                await _itemRepository.InsertAsync(item, autoSave: true);
            }

            return group.Id;
        });
    }

    /// <summary>Fiziksel stok girişi (Normal Giriş) — opsiyonel VARYANT snapshot'lı voucher satırı
    /// (stok raporunun (MetalId, VariantId) kırılımının beslemesi; null = legacy varyantsız hareket).</summary>
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
}
