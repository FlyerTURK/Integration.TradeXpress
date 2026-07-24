using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil hesaplama beslemesi (M3) — <see cref="ISubstitutionCalculationAppService"/> uçtan uca:
/// gerçek grup + maden kataloğu + voucher-beslemeli stok raporu üzerinden solver'ın kullanıcı
/// tablosuna (tüm denemeler + ön-filtre + özet) doğru DTO'landığı ve REZERVE stokun solver'a
/// hiç girmediği (kullanılabilir = Net − RezerveÇıkış) pinlenir. Konsept 12gr örneğinin
/// küçültülmüş versiyonu: talep 12gr; stok 3×10gr, 7×5gr, 20×1gr (+ 2×20gr ön-filtrede elenir).
/// Maliyet FAIL-FAST'tir (2026-07-10 kullanıcı kararı): kur çözülebilen şirkette (TRY ülkesi seed'li)
/// GERÇEK maliyet sıralaması pinlenir; kur çözülemeyen şirkette hesap <c>RatesMissing</c> ile HİÇ koşmaz.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class SubstitutionCalculationTests : TradeXpressEntityFrameworkCoreTestBase
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
    private readonly ICurrentTenant _currentTenant;

    public SubstitutionCalculationTests()
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
        _currentTenant                = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Calculate_enumerates_combinations_prefilters_and_ranks_from_real_stock()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SBA"));
        _companyContext.CompanyId = data.CompanyId;

        // Kur seed'i: yerel para çözülebilen ülke (TRY) → değerleme dolu (tüm birimler 1/1) → maliyet ölçütü CANLI.
        await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, "SBA"));

        // Katalog: adet-hesaplı standart gramajlı madenler (10gr / 5gr / 1gr / 20gr). İşçilik maliyet
        // sıralamasını AYRIŞTIRIR (kurlar 1/1 iken salt gram maliyeti tüm 12gr kombinasyonlarında eşitti):
        // parça maliyeti = gram × 1 + adet-başı işçilik → 10gr=10 · 5gr=6 · 1gr=3 TRY.
        var ten    = await SeedMetalAsync(data, "SBATEN",    10m);
        var five   = await SeedMetalAsync(data, "SBAFIVE",   5m, entryLaborPerPiece: 1m, laborUnitId: data.TryUnitId);
        var one    = await SeedMetalAsync(data, "SBAONE",    1m, entryLaborPerPiece: 2m, laborUnitId: data.TryUnitId);
        var twenty = await SeedMetalAsync(data, "SBATWENTY", 20m);

        // Stok (fiziksel giriş): 3×10gr, 7×5gr, 20×1gr, 2×20gr.
        await SeedInboundStockAsync(data, ten,    count: 3);
        await SeedInboundStockAsync(data, five,   count: 7);
        await SeedInboundStockAsync(data, one,    count: 20);
        await SeedInboundStockAsync(data, twenty, count: 2);

        // Grup: tüketim önceliği 10gr → 5gr → 1gr → 20gr; tolerans grup varsayılanı (Gram, 0 = mutlak eşitlik).
        var groupId = await SeedGroupAsync(data, "SBAGRP", ten.Id, five.Id, one.Id, twenty.Id);

        var result = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId = groupId,
            TargetQuantity      = 12m,
            BranchId            = data.BranchId,
        });

        // Özet + tolerans (grup ayarı esas).
        result.TargetQuantity.ShouldBe(12m);
        result.EffectiveTolerance.ShouldBe(0m);
        result.TrialCount.ShouldBe(result.Trials.Count);
        result.SuccessCount.ShouldBe(result.Trials.Count(t => t.Success));
        result.SuccessCount.ShouldBeGreaterThanOrEqualTo(4);   // 1×10+2×1 · 2×5+2×1 · 1×5+7×1 · 12×1 ...

        // Ön-filtre: 20gr tek parçası talebi aşar → elenir + raporlanır; denemelere hiç girmez.
        var filtered = result.FilteredOut.ShouldHaveSingleItem();
        filtered.MetalCode.ShouldBe("SBATWENTY");
        filtered.Reason.ShouldBe(SubstitutionReasonCodes.PieceWeightExceedsTarget);
        result.Trials.ShouldAllBe(t => t.Lines.All(l => l.MetalId != twenty.Id));

        // İlk deneme = açgözlü doldurma (konsept SSOT): 1×10 + 2×1 = 12 ✓ — parça maliyetleri gerçek
        // (10gr → 10 TRY; 1gr → 1 + 2 işçilik = 3 TRY), toplam 10 + 2×3 = 16 TRY.
        var first = result.Trials.First();
        first.Success.ShouldBeTrue();
        first.TotalWeight.ShouldBe(12m);
        first.Deviation.ShouldBe(0m);
        first.Lines.Select(l => (l.MetalCode, l.Count, l.UnitCost)).ShouldBe(new[]
        {
            ("SBATEN", 1, 10m),
            ("SBAONE", 2, 3m),
        });
        first.TotalCost.ShouldBe(16m);

        // Tüm başarılılar mutlak eşit (tolerans 0) + Rank'lı; başarısızlar nedenli + Rank'sız.
        result.Trials.Where(t => t.Success).ShouldAllBe(t => t.TotalWeight == 12m && t.Rank != null);
        result.Trials.Where(t => !t.Success).ShouldAllBe(t => t.FailureReason != null && t.Rank == null);

        // Maliyet para birimi ülke biriminden çözüldü (fail-fast geçildi → daima dolu).
        result.CostCurrencyCode.ShouldBe(CurrencyUnitCode.TRY);

        // GERÇEK maliyet sıralaması (skor 1. ölçüt): 1×10+2×1=16 < 2×5+2×1=18 < 1×5+7×1=27 < 12×1=36 TRY.
        result.Trials.Where(t => t.Success)
            .OrderBy(t => t.Rank)
            .Select(t => t.TotalCost)
            .ShouldBe(new[] { 16m, 18m, 27m, 36m });

        // Rank 1 = en düşük maliyetli 1×10+2×1 (3 parça); paket sayısı = min(3/1, 20/2) = 3.
        var best = result.Trials.Single(t => t.Rank == 1);
        best.PieceCount.ShouldBe(3);
        best.PackageCount.ShouldBe(3);
        best.IsTopCandidate.ShouldBeTrue();
        best.Lines.Select(l => (l.MetalCode, l.Count)).ShouldBe(new[] { ("SBATEN", 1), ("SBAONE", 2) });
    }

    [Fact]
    public async Task Calculate_fails_fast_when_local_currency_or_rates_cannot_be_resolved()
    {
        // Şirketin ülkesi SENTETİK id (SeedCompanyGraphAsync varsayılanı) → yerel para çözülemez →
        // hesap HİÇ koşmaz: RatesMissing + eksik maden KODLARI (2026-07-10 fail-fast kararı; 0-maliyet katılım YOK).
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SBM"));
        _companyContext.CompanyId = data.CompanyId;

        var ten = await SeedMetalAsync(data, "SBMTEN", 10m);
        var one = await SeedMetalAsync(data, "SBMONE", 1m);
        await SeedInboundStockAsync(data, ten, count: 3);
        await SeedInboundStockAsync(data, one, count: 20);

        var groupId = await SeedGroupAsync(data, "SBMGRP", ten.Id, one.Id);

        var ratesMissing = await Should.ThrowAsync<BusinessException>(() =>
            _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
            {
                SubstitutionGroupId = groupId,
                TargetQuantity      = 12m,
                BranchId            = data.BranchId,
            }));

        ratesMissing.Code.ShouldBe("TradeXpress:Substitution:RatesMissing");
        ratesMissing.Data["metalCodes"].ShouldBe("SBMTEN, SBMONE");
    }

    [Fact]
    public async Task Calculate_feeds_solver_with_available_quantity_excluding_reserved_out()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SBR"));
        _companyContext.CompanyId = data.CompanyId;
        await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, "SBR"));

        // 5gr'lık madenden 50 adet fiziksel giriş, 40 adedi müşteriye REZERVE (çıkış rezervasyonu)
        // → kullanılabilir 10 adet. Rezervasyon sızarsa 50 görünür ve 100gr talebi 20 parçayla TUTARDI.
        var metal = await SeedMetalAsync(data, "SBRFIVE", 5m);
        await SeedInboundStockAsync(data, metal, count: 50);
        await _voucherAppService.SaveLineAsync(MetalLine(
            data, metal, ProcessDirectionType.Outbound, ProcessPaymentType.Reservation, count: 40));

        var groupId = await SeedGroupAsync(data, "SBRGRP", metal.Id);

        var result = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
        {
            SubstitutionGroupId = groupId,
            TargetQuantity      = 100m,
            BranchId            = data.BranchId,
        });

        // Solver yalnız 10 adet görür: kapasite 10×5 = 50gr < 100gr → toplam-kapasite kısa devresi
        // (2026-07-10 kararı) numaralandırmayı HİÇ başlatmaz. TotalAvailableWeight=50 rezervasyonun
        // düşüldüğünün kanıtı — sızsaydı 50 adet × 5gr = 250gr kapasiteyle hesap KOŞARDI.
        result.SuccessCount.ShouldBe(0);
        result.InsufficientStock.ShouldBeTrue();
        result.TotalAvailableWeight.ShouldBe(50m);
        result.Trials.ShouldBeEmpty();
    }

    [Fact]
    public async Task Calculate_includes_host_level_labor_in_costs_under_tenant_working_context()
    {
        // ÜRETİM DÜZENİ (14 tenant tek DB): maden katalogu + varyant işçiliği HOST-seviyesi (TenantId=null),
        // operasyon (şirket + stok + grup) TENANT altında. İşçilik join'i IMultiTenant/ICompanyScoped filtreleri
        // kapatılmadan koşarsa host satırları elenir → EntryLabor sessizce 0 olur ve sıralama salt gram maliyetine
        // çöker (maskeleme). Bu test tenant working-context'inde işçiliğin HESABA GİRDİĞİNİ pinler.
        var tenantId = SimpleGuidGenerator.Instance.Create();

        VoucherTestData data;
        using (_currentTenant.Change(tenantId))
        {
            data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SBT"));
            _companyContext.CompanyId = data.CompanyId;
            await WithUnitOfWorkAsync(() => _seeder.AttachLocalCurrencyCountryAsync(data, "SBT"));
        }

        // Katalog HOST bağlamında (ambient tenant yok → TenantId=null): metal + ana varyant + işçilik detayı.
        // 1gr madenin ADET-BAŞI 2 TRY işçiliği maliyet sıralamasını ayrıştırır (kurlar 1/1).
        var ten = await SeedMetalAsync(data, "SBTTEN", 10m);
        var one = await SeedMetalAsync(data, "SBTONE", 1m, entryLaborPerPiece: 2m, laborUnitId: data.TryUnitId);

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = data.CompanyId;

            await SeedInboundStockAsync(data, ten, count: 3);
            await SeedInboundStockAsync(data, one, count: 20);
            var groupId = await SeedGroupAsync(data, "SBTGRP", ten.Id, one.Id);

            var result = await _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
            {
                SubstitutionGroupId = groupId,
                TargetQuantity      = 12m,
                BranchId            = data.BranchId,
            });

            // İşçilik hesaba GİRDİ: 1gr parça maliyeti 1 (gram) + 2 (adet-başı işçilik) = 3 TRY — host
            // işçilik satırları tenant filtresine takılsaydı 1 TRY görünürdü. İlk deneme (açgözlü):
            // 1×10 + 2×1 → 10 + 2×3 = 16 TRY.
            result.CostCurrencyCode.ShouldBe(CurrencyUnitCode.TRY);
            var first = result.Trials.First();
            first.Success.ShouldBeTrue();
            first.TotalWeight.ShouldBe(12m);
            first.Lines.Select(l => (l.MetalCode, l.Count, l.UnitCost)).ShouldBe(new[]
            {
                ("SBTTEN", 1, 10m),
                ("SBTONE", 2, 3m),
            });
            first.TotalCost.ShouldBe(16m);

            // Sıralama işçilikli maliyeti yansıtır: 1×10+2×1=16 < 12×1=36 TRY. İşçilik 0'a maskelenseydi
            // iki deneme de 12 TRY'ye eşitlenir, sıralama işçilikten bağımsızlaşırdı.
            result.Trials.Where(t => t.Success)
                .OrderBy(t => t.Rank)
                .Select(t => t.TotalCost)
                .ShouldBe(new[] { 16m, 36m });
        }
    }

    [Fact]
    public async Task Calculate_fails_fast_on_invalid_target_inactive_group_and_empty_group()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("SBG"));
        _companyContext.CompanyId = data.CompanyId;

        // TargetQuantity ≤ 0 → DB'ye gitmeden fail-fast (solver'la aynı hata kodu).
        var invalidTarget = await Should.ThrowAsync<BusinessException>(() =>
            _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
            {
                SubstitutionGroupId = Guid.NewGuid(),   // red-senaryo: erişilmeden reddedilir
                TargetQuantity      = 0m,
            }));
        invalidTarget.Code.ShouldBe("TradeXpress:Substitution:RequestedAmountInvalid");

        // Grup yok → GroupNotFound.
        var notFound = await Should.ThrowAsync<BusinessException>(() =>
            _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
            {
                SubstitutionGroupId = Guid.NewGuid(),   // red-senaryo: mevcut olmayan id
                TargetQuantity      = 5m,
            }));
        notFound.Code.ShouldBe("TradeXpress:Substitution:GroupNotFound");

        // Pasif grup → GroupNotActive.
        var inactiveGroupId = await WithUnitOfWorkAsync(async () =>
        {
            var group = new SubstitutionGroup(data.CompanyId, "SBGPASSIVE", "Passive Group");
            group.SetActive(false);
            return (await _groupRepository.InsertAsync(group, autoSave: true)).Id;
        });
        var inactive = await Should.ThrowAsync<BusinessException>(() =>
            _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
            {
                SubstitutionGroupId = inactiveGroupId,
                TargetQuantity      = 5m,
            }));
        inactive.Code.ShouldBe("TradeXpress:Substitution:GroupNotActive");

        // Satırsız grup → GroupHasNoItems.
        var emptyGroupId = await WithUnitOfWorkAsync(async () =>
        {
            var group = new SubstitutionGroup(data.CompanyId, "SBGEMPTY", "Empty Group");
            return (await _groupRepository.InsertAsync(group, autoSave: true)).Id;
        });
        var empty = await Should.ThrowAsync<BusinessException>(() =>
            _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
            {
                SubstitutionGroupId = emptyGroupId,
                TargetQuantity      = 5m,
            }));
        empty.Code.ShouldBe("TradeXpress:Substitution:GroupHasNoItems");

        // Adet-hesapsız maden içeren grup → MetalNotPieceTracked (metal referansı geçerli ama gramajsız).
        var looseMetalGroupId = await WithUnitOfWorkAsync(async () =>
        {
            var looseMetal = await _metalRepository.InsertAsync(
                new Metal("SBGLOOSE", "Loose Metal", data.HasUnitId, factor: 1m),
                autoSave: true);
            var group = await _groupRepository.InsertAsync(
                new SubstitutionGroup(data.CompanyId, "SBGLOOSEGRP", "Loose Group"), autoSave: true);
            await _itemRepository.InsertAsync(
                new SubstitutionGroupItem(data.CompanyId, group.Id, looseMetal.Id), autoSave: true);
            return group.Id;
        });
        var notPieceTracked = await Should.ThrowAsync<BusinessException>(() =>
            _calculationAppService.CalculateAsync(new SubstitutionCalculationInput
            {
                SubstitutionGroupId = looseMetalGroupId,
                TargetQuantity      = 5m,
            }));
        notPieceTracked.Code.ShouldBe("TradeXpress:Substitution:MetalNotPieceTracked");
    }

    // ── seed yardımcıları ──────────────────────────────────────────────────────────

    /// <summary>Adet-hesaplı + standart gramajlı maden kataloğu kaydı (HAS takipli, milyem 1) + ana varyantı.
    /// İşçilik artık madende DEĞİL, ana varyantın <see cref="MetalVariantDetail"/> uzantısındadır (solver bunu
    /// EntityVariant→MetalVariantDetail join'iyle okur). Opsiyonel ADET-BAŞI işçilik (LaborType=Quantity)
    /// maliyet sıralamasını ayrıştırmak için ana varyanta yazılır (0 işçilik = labor bacağı yok).</summary>
    private Task<Metal> SeedMetalAsync(
        VoucherTestData data,
        string code,
        decimal pieceWeight,
        decimal entryLaborPerPiece = 0m,
        Guid? laborUnitId = null)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var metal = await _metalRepository.InsertAsync(
                new Metal(code, $"{code} Metal", data.HasUnitId, factor: 1m,
                    isQuantity: true, stableQuantity: pieceWeight),
                autoSave: true);

            await SeedMainVariantWithLaborAsync(metal, entryLaborPerPiece, laborUnitId);

            return metal;
        });
    }

    /// <summary>Madenin ANA varyantını (EntityName="Metal", EntityId=Metal.Id, IsMain) + adet-başı işçilik
    /// taşıyan <see cref="MetalVariantDetail"/>'ini kurar — production'daki maden-varyant deseniyle aynı
    /// (varyant tenant-geneli: CompanyId=null). Solver labor'ı bu ana varyanttan çözer.</summary>
    private async Task SeedMainVariantWithLaborAsync(Metal metal, decimal entryLaborPerPiece, Guid? laborUnitId)
    {
        var variant = await _entityVariantRepository.InsertAsync(
            new EntityVariant(
                companyId: null, entityName: "Metal", entityId: metal.Id,
                code: $"{metal.Code}-MAIN", name: $"{metal.Name} Main", isMain: true),
            autoSave: true);

        var detail = new MetalVariantDetail(companyId: null, entityVariantId: variant.Id);
        detail.SetLabor(
            MetalLaborType.Quantity, laborTypeChange: false,
            entryLabor: entryLaborPerPiece, entryLaborUnitId: laborUnitId, entryLaborChange: false,
            exitLabor: 0m, exitLaborUnitId: null, exitLaborChange: false,
            costUnitId: null);
        await _metalVariantDetailRepository.InsertAsync(detail, autoSave: true);
    }

    /// <summary>Muadil grubu + DisplayOrder sıralı maden satırları (parametre sırası = tüketim önceliği).</summary>
    private Task<Guid> SeedGroupAsync(VoucherTestData data, string code, params Guid[] metalIds)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var group = await _groupRepository.InsertAsync(
                new SubstitutionGroup(data.CompanyId, code, $"{code} Group"), autoSave: true);
            for (var order = 0; order < metalIds.Length; order++)
            {
                await _itemRepository.InsertAsync(
                    new SubstitutionGroupItem(data.CompanyId, group.Id, metalIds[order], displayOrder: order),
                    autoSave: true);
            }

            return group.Id;
        });
    }

    /// <summary>Fiziksel stok girişi: Normal Giriş, adet × parça gramı (stok raporu AvailableQuantity beslemesi).</summary>
    private Task SeedInboundStockAsync(VoucherTestData data, Metal metal, int count)
    {
        return _voucherAppService.SaveLineAsync(MetalLine(
            data, metal, ProcessDirectionType.Inbound, ProcessPaymentType.Normal, count));
    }

    /// <summary>Madene bağlı (CommodityId'li) maden satırı — stok raporu MetalId kırılımının beslemesi.</summary>
    private static VoucherLineDto MetalLine(
        VoucherTestData data, Metal metal, ProcessDirectionType direction, ProcessPaymentType paymentType, int count)
    {
        var amount = count * metal.StableQuantity;
        return new VoucherLineDto
        {
            BranchId      = data.BranchId,
            VaultId       = data.VaultId,
            AccountId     = data.AccountId,
            SubAccountId  = data.SubAccountId,
            Type          = ProcessType.Metal,
            Direction     = direction,
            PaymentType   = paymentType,
            CommodityId   = metal.Id,
            CommodityCode = metal.Code,
            Quantity      = count,
            Amount        = amount,
            Factor        = 1m,
            Total         = amount,
            MainUnitId    = data.HasUnitId,
        };
    }
}
