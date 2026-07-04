using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.ExchangeRates;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Reports.BalanceSheet;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Bilanço motoru davranış pinleri (E-5'te ertelenen; K4 SQL-side aggregation refactor'unun güvenlik ağı):
/// <list type="bullet">
///   <item>Cash + Metal fişleri → ComputeAsync kategori×birim satırları (AccountBalance ledger'dan,
///         Stock/Labor voucher'dan) — beklenenler ELLE hesaplı, alış-anı break-even (TOPLAM=0) dahil.</item>
///   <item>İşçilik ağırlıklı-ortalama maliyeti (kısmi satışta on-hand oranı) + satış kârının TOPLAM'a düşmesi.</item>
///   <item>Branch vs Company kapsam farkı (şube filtreli / konsolide).</item>
///   <item>SaveAsync → snapshot yazımı (idempotent) + GetSnapshotListAsync PIVOT/running türetimleri.</item>
///   <item>ResetProfitPeriodAsync → Branch.ProfitResetDate ilerler + snapshot donar (kapsama göre şube seti).</item>
/// </list>
/// Kurlar deterministik: host seed TRY=1/1; HAS için testte 5000/5000 host ExchangeRate satırı eklenir
/// (marj Passthrough → efektif 5000; TRY marjı Fixed(1) → efektif 1; base=TRY → HAS değerleme kuru 5000).
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class BalanceSheetComputeTests : TradeXpressEntityFrameworkCoreTestBase
{
    /// <summary>Deterministik HAS değerleme kuru (test ExchangeRate satırı; TRY base'e re-base sonrası aynı).</summary>
    private const decimal HasRate = 5000m;

    /// <summary>Maden satırlarının Commodity referansı — GetMetalLaborByUnitAsync <c>CommodityId != null</c> ister.</summary>
    private static readonly Guid GoldCommodityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IBalanceSheetReportAppService _balanceSheet;
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly IRepository<ExchangeRate, Guid> _rateRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<BalanceSheetSnapshot, Guid> _snapshotRepository;

    public BalanceSheetComputeTests()
    {
        _balanceSheet         = GetRequiredService<IBalanceSheetReportAppService>();
        _voucherAppService    = GetRequiredService<IVoucherAppService>();
        _seeder               = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext       = GetRequiredService<TestCompanyContextProvider>();
        _rateRepository       = GetRequiredService<IRepository<ExchangeRate, Guid>>();
        _branchRepository     = GetRequiredService<IRepository<Branch, Guid>>();
        _vaultRepository      = GetRequiredService<IRepository<Vault, Guid>>();
        _subAccountRepository = GetRequiredService<IRepository<SubAccount, Guid>>();
        _snapshotRepository   = GetRequiredService<IRepository<BalanceSheetSnapshot, Guid>>();
    }

    [Fact]
    public async Task Compute_purchase_only_produces_manual_expected_rows_and_break_even_total()
    {
        var data = await ArrangeCompanyAsync();

        // Nakit GİRİŞ +1000 TRY; Maden GİRİŞ 10 HAS + 150 TRY işçilik (Normal).
        await SaveCashAsync(data, ProcessDirectionType.Inbound, 1000m);
        await SaveMetalAsync(data, ProcessDirectionType.Inbound, hasTotal: 10m, laborTotal: 150m);

        var result = await ComputeAsync(BalanceSheetScope.Company);

        result.BaseCurrencyCode.ShouldBe(CurrencyUnitCode.TRY);
        result.Rows.Count.ShouldBe(5);

        // BAKİYE = −Σ(ledger): TRY ledger'ı +1000(nakit) +150(işçilik) → −1150; HAS +10 → −10.
        AssertRow(result, BalanceSheetCategory.AccountBalance, data.TryUnitId, amount: -1150m, rate: 1m,       net: -1150m);
        AssertRow(result, BalanceSheetCategory.AccountBalance, data.HasUnitId, amount: -10m,   rate: HasRate, net: -50_000m);

        // STOK = fiziksel holding (+): nakit +1000 TRY; maden +10 HAS (Effect×Factor).
        AssertRow(result, BalanceSheetCategory.Stock, data.TryUnitId, amount: 1000m, rate: 1m,       net: 1000m);
        AssertRow(result, BalanceSheetCategory.Stock, data.HasUnitId, amount: 10m,   rate: HasRate, net: 50_000m);

        // İŞÇİLİK = on-hand işçilik maliyeti (satış yok → girişin tamamı).
        AssertRow(result, BalanceSheetCategory.Labor, data.TryUnitId, amount: 150m, rate: 1m, net: 150m);

        // Kategori toplamları + alış-anı break-even (ERPPRO BAKİYE+İŞÇİLİK+STOK = 0 paritesi).
        CategoryNet(result, BalanceSheetCategory.AccountBalance).ShouldBe(-51_150m);
        CategoryNet(result, BalanceSheetCategory.Stock).ShouldBe(51_000m);
        CategoryNet(result, BalanceSheetCategory.Labor).ShouldBe(150m);
        result.CategoryTotals.ShouldAllBe(t => t.CountsInTotal);
        result.Total.ShouldBe(0m);
    }

    [Fact]
    public async Task Compute_partial_sale_uses_weighted_average_labor_cost_and_surfaces_profit_in_total()
    {
        var data = await ArrangeCompanyAsync();

        await SaveCashAsync(data, ProcessDirectionType.Inbound, 1000m);
        await SaveMetalAsync(data, ProcessDirectionType.Inbound,  hasTotal: 10m, laborTotal: 150m);   // alış: 10 HAS, işçilik 150
        await SaveMetalAsync(data, ProcessDirectionType.Outbound, hasTotal: 5m,  laborTotal: 100m);   // satış: 5 HAS, işçilik 100

        var result = await ComputeAsync(BalanceSheetScope.Company);

        // BAKİYE: TRY = −(1000 + 150 − 100) = −1050; HAS = −(10 − 5) = −5.
        AssertRow(result, BalanceSheetCategory.AccountBalance, data.TryUnitId, amount: -1050m, rate: 1m,       net: -1050m);
        AssertRow(result, BalanceSheetCategory.AccountBalance, data.HasUnitId, amount: -5m,    rate: HasRate, net: -25_000m);

        // STOK: HAS on-hand 5; nakit 1000.
        AssertRow(result, BalanceSheetCategory.Stock, data.HasUnitId, amount: 5m,    rate: HasRate, net: 25_000m);
        AssertRow(result, BalanceSheetCategory.Stock, data.TryUnitId, amount: 1000m, rate: 1m,      net: 1000m);

        // İŞÇİLİK ağırlıklı-ortalama (ERPPRO GetMadenMaliyeti): 150 × (10−5)/10 = 75 — satış fiyatıyla (100) DÜŞMEZ.
        AssertRow(result, BalanceSheetCategory.Labor, data.TryUnitId, amount: 75m, rate: 1m, net: 75m);

        // TOPLAM = işçilik marj kârı: satış işçiliği 100 − maliyeti 75 = 25.
        result.Total.ShouldBe(25m);
    }

    [Fact]
    public async Task Branch_scope_filters_to_branch_while_company_scope_consolidates()
    {
        var data = await ArrangeCompanyAsync();
        var second = await SeedSecondBranchAsync(data);

        // Şube-1'e 1000 TRY, şube-2'ye 400 TRY nakit girişi.
        await SaveCashAsync(data, ProcessDirectionType.Inbound, 1000m);
        var cash2 = VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 400m);
        cash2.BranchId     = second.BranchId;
        cash2.VaultId      = second.VaultId;
        cash2.SubAccountId = second.SubAccountId;
        await _voucherAppService.SaveLineAsync(cash2);

        // Company scope = konsolide (1000 + 400).
        var company = await ComputeAsync(BalanceSheetScope.Company);
        AssertRow(company, BalanceSheetCategory.Stock,          data.TryUnitId, amount: 1400m,  rate: 1m, net: 1400m);
        AssertRow(company, BalanceSheetCategory.AccountBalance, data.TryUnitId, amount: -1400m, rate: 1m, net: -1400m);

        // Branch scope = yalnız o şube.
        var branch1 = await ComputeAsync(BalanceSheetScope.Branch, data.BranchId);
        AssertRow(branch1, BalanceSheetCategory.Stock, data.TryUnitId, amount: 1000m, rate: 1m, net: 1000m);

        var branch2 = await ComputeAsync(BalanceSheetScope.Branch, second.BranchId);
        AssertRow(branch2, BalanceSheetCategory.Stock, data.TryUnitId, amount: 400m, rate: 1m, net: 400m);
    }

    [Fact]
    public async Task Save_persists_snapshot_idempotently_and_snapshot_list_pivots_with_running_derivations()
    {
        var data = await ArrangeCompanyAsync();

        await SaveCashAsync(data, ProcessDirectionType.Inbound, 1000m);
        await SaveMetalAsync(data, ProcessDirectionType.Inbound,  hasTotal: 10m, laborTotal: 150m);
        await SaveMetalAsync(data, ProcessDirectionType.Outbound, hasTotal: 5m,  laborTotal: 100m);

        var filter = new BalanceSheetReportFilterDto { Scope = BalanceSheetScope.Company, AsOf = DateTime.Today };
        var saved = await _balanceSheet.SaveAsync(filter);

        // ComputeAsync detay satırlarının TAMAMI (5 kategori×birim) snapshot'a donar.
        saved.Rows.Count.ShouldBe(5);
        (await GetSnapshotsAsync(data.CompanyId)).Count.ShouldBe(5);

        // İdempotent: aynı gün+kapsam yeniden kaydetmek çoğaltmaz (sil + yeniden yaz).
        await _balanceSheet.SaveAsync(filter);
        var snapshots = await GetSnapshotsAsync(data.CompanyId);
        snapshots.Count.ShouldBe(5);
        snapshots.ShouldAllBe(s => s.Scope == BalanceSheetScope.Company && s.BranchId == null);
        // AsOfDate wall-clock (kaymasız): [DisableDateTimeNormalization] → UTC'ye çevrilmez, gün doğrudan bugün.
        // (Eski assertion ToLocalTime() ile hatalı UTC-normalizasyonunu kodluyordu; artık kayma yok, direkt .Date.)
        snapshots.ShouldAllBe(s => s.AsOfDate.Date == DateTime.Today);

        // PIVOT liste: tek gün → CategoryNets + TOPLAM + running türetimler (ilk gün: DEVIR=0, KARZARAR=TOPLAM).
        var list = await _balanceSheet.GetSnapshotListAsync(
            new BalanceSheetSnapshotListRequestDto { Scope = BalanceSheetScope.Company });
        var row = list.Rows.ShouldHaveSingleItem();
        row.AsOfDate.Date.ShouldBe(DateTime.Today);   // wall-clock, kaymasız ([DisableDateTimeNormalization])
        row.BaseCurrencyCode.ShouldBe(CurrencyUnitCode.TRY);
        row.CategoryNets[BalanceSheetCategory.AccountBalance].ShouldBe(-26_050m);   // −1050 + −25000
        row.CategoryNets[BalanceSheetCategory.Stock].ShouldBe(26_000m);             // 1000 + 25000
        row.CategoryNets[BalanceSheetCategory.Labor].ShouldBe(75m);
        row.Total.ShouldBe(25m);
        row.Devir.ShouldBe(0m);
        row.KarZarar.ShouldBe(25m);
        row.Masraf.ShouldBe(0m);
    }

    [Fact]
    public async Task Reset_profit_period_advances_branch_reset_date_and_freezes_snapshot()
    {
        var data = await ArrangeCompanyAsync();
        var second = await SeedSecondBranchAsync(data);

        await SaveCashAsync(data, ProcessDirectionType.Inbound, 1000m);

        // Branch scope: yalnız o şubenin ProfitResetDate'i ilerler + Branch-scope snapshot donar.
        var branchFilter = new BalanceSheetReportFilterDto
        {
            Scope    = BalanceSheetScope.Branch,
            BranchId = data.BranchId,
            AsOf     = DateTime.Today,
        };
        await _balanceSheet.ResetProfitPeriodAsync(branchFilter);

        AssertResetToToday(await GetBranchAsync(data.BranchId));
        (await GetBranchAsync(second.BranchId)).ProfitResetDate.ShouldBeNull();

        var branchSnapshots = await GetSnapshotsAsync(data.CompanyId);
        branchSnapshots.ShouldNotBeEmpty();
        branchSnapshots.ShouldAllBe(s => s.Scope == BalanceSheetScope.Branch && s.BranchId == data.BranchId);

        // Company scope: şirketin TÜM şubeleri ilerler + konsolide snapshot donar.
        await _balanceSheet.ResetProfitPeriodAsync(
            new BalanceSheetReportFilterDto { Scope = BalanceSheetScope.Company, AsOf = DateTime.Today });

        AssertResetToToday(await GetBranchAsync(second.BranchId));
        (await GetSnapshotsAsync(data.CompanyId))
            .ShouldContain(s => s.Scope == BalanceSheetScope.Company && s.BranchId == null);
    }

    // ── kurulum yardımcıları ────────────────────────────────────────────────────

    /// <summary>Org grafını kurar, working şirketi ayarlar, HAS'a deterministik host kuru (5000/5000) ekler.</summary>
    private async Task<VoucherTestData> ArrangeCompanyAsync()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());
        _companyContext.CompanyId = data.CompanyId;

        await WithUnitOfWorkAsync(() => _rateRepository.InsertAsync(
            new ExchangeRate(
                data.HasUnitId,
                marketPriceOnBuy: HasRate,
                marketPriceOnSell: HasRate,
                appliedMarginOnBuy: MarginSetting.Passthrough,
                appliedMarginOnSell: MarginSetting.Passthrough,
                source: "Test",
                rateDate: DateTime.UtcNow),
            autoSave: true));

        return data;
    }

    /// <summary>İkinci şube + kasa + althesap (Branch vs Company kapsam senaryoları).</summary>
    private async Task<(Guid BranchId, Guid VaultId, Guid SubAccountId)> SeedSecondBranchAsync(VoucherTestData data)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var branch = await _branchRepository.InsertAsync(
                new Branch(data.CompanyId, "TSTBR2", "TST Branch 2", isHeadquarters: false), autoSave: true);
            var vault = await _vaultRepository.InsertAsync(
                new Vault(data.CompanyId, branch.Id, "TSTVLT2", "TST Vault 2", isDefault: true), autoSave: true);
            var sub = await _subAccountRepository.InsertAsync(
                new SubAccount(data.CompanyId, data.AccountId, branch.Id, "TSTSUB2", "TST Sub Account 2"), autoSave: true);
            return (branch.Id, vault.Id, sub.Id);
        });
    }

    private Task SaveCashAsync(VoucherTestData data, ProcessDirectionType direction, decimal payTotal)
    {
        return _voucherAppService.SaveLineAsync(VoucherTestLines.CashLine(data, direction, payTotal));
    }

    /// <summary>Maden satırı kaydeder; işçilik hesabı (GetMetalLaborByUnitAsync) CommodityId ister → sabit atanır.</summary>
    private Task SaveMetalAsync(VoucherTestData data, ProcessDirectionType direction, decimal hasTotal, decimal laborTotal)
    {
        var dto = VoucherTestLines.MetalLine(data, direction, hasTotal, laborTotal);
        dto.CommodityId = GoldCommodityId;
        return _voucherAppService.SaveLineAsync(dto);
    }

    private Task<BalanceSheetReportResultDto> ComputeAsync(BalanceSheetScope scope, Guid? branchId = null)
    {
        return _balanceSheet.ComputeAsync(new BalanceSheetReportFilterDto
        {
            Scope    = scope,
            BranchId = branchId,
            AsOf     = DateTime.Today,
        });
    }

    private Task<List<BalanceSheetSnapshot>> GetSnapshotsAsync(Guid companyId)
    {
        return WithUnitOfWorkAsync(() => _snapshotRepository.GetListAsync(s => s.CompanyId == companyId));
    }

    private Task<Branch> GetBranchAsync(Guid branchId)
    {
        return WithUnitOfWorkAsync(() => _branchRepository.GetAsync(branchId));
    }

    // ── assert yardımcıları ─────────────────────────────────────────────────────

    private static void AssertRow(
        BalanceSheetReportResultDto result, string category, Guid unitId, decimal amount, decimal rate, decimal net)
    {
        var row = result.Rows.SingleOrDefault(r => r.Category == category && r.UnitId == unitId);
        row.ShouldNotBeNull($"satır yok: {category} / {unitId}");
        row.Amount.ShouldBe(amount);
        row.ValuationRate.ShouldBe(rate);
        row.Net.ShouldBe(net);
        row.MissingRate.ShouldBeFalse();
    }

    /// <summary>ProfitResetDate bugüne ilerledi mi — wall-clock, kaymasız ([DisableDateTimeNormalization]).</summary>
    private static void AssertResetToToday(Branch branch)
    {
        branch.ProfitResetDate.ShouldNotBeNull();
        // Eski assertion ToLocalTime() ile UTC-normalizasyonunu kodluyordu; artık date-only wall-clock → direkt .Date.
        branch.ProfitResetDate.Value.Date.ShouldBe(DateTime.Today);
    }

    private static decimal CategoryNet(BalanceSheetReportResultDto result, string category)
    {
        return result.CategoryTotals.Single(t => t.Category == category).Net;
    }
}
