using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Reports.BalanceSheet;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Vouchers.Balance;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// <see cref="ICompanyOwned"/> (finansal çekirdek GÜVENLİK SINIRI) global query filter regresyon ağı —
/// Faz 0 Account örneğinin Voucher ailesine yayılması. <see cref="ICompanyScoped"/> (görünüm) filtresinden
/// TEK yapısal fark: "holding-host (CompanyId=null)" görünür kolu YOKTUR — kayıt DAİMA tek şirkete aittir,
/// yalnız kendi şirketine görünür. Working şirket doluyken yabancı şirketin kaydı yapısal GÖRÜNMEZ (unutulan
/// AppService sorgusunda bile sızmaz); host kaydı (TenantId=null) tenant filtresi kapalıyken company'den de
/// muaf; konsolide (working şirket yok) PERMISSIVE = tenant'ın tüm şirketleri. Anahtar
/// <see cref="ICompanyScoped"/> ile PAYLAŞILIR (iki marker tek IDataFilter anahtarı).
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class CompanyOwnedFilterTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IRepository<Voucher, Guid> _vouchers;
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledger;
    private readonly IRepository<BalanceSheetSnapshot, Guid> _snapshots;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;

    public CompanyOwnedFilterTests()
    {
        _vouchers       = GetRequiredService<IRepository<Voucher, Guid>>();
        _ledger         = GetRequiredService<IRepository<BalanceLedgerEntry, Guid>>();
        _snapshots      = GetRequiredService<IRepository<BalanceSheetSnapshot, Guid>>();
        _seeder         = GetRequiredService<VoucherTestDataSeeder>();
        _dataFilter     = GetRequiredService<IDataFilter>();
        _currentTenant  = GetRequiredService<ICurrentTenant>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    /// <summary>Voucher gerçek FK taşır (Company/Branch/Vault/Account/SubAccount) → id-only ledger/snapshot'tan
    /// farklı olarak geçerli org grafı gerekir; bu yüzden ortak <see cref="VoucherTestDataSeeder"/> ile üç şirket
    /// grafı kurulur (host + tenant altı A/B), her birine bir fiş yazılır.</summary>
    [Fact]
    public async Task Voucher_owned_filter_isolates_companies()
    {
        _companyContext.CompanyId = null; // seed sırasında context yok (auto-stamp yok; kapsam ctor'dan)

        var suffix   = SimpleGuidGenerator.Instance.Create().ToString("N")[..6].ToUpperInvariant();
        var tenantId = SimpleGuidGenerator.Instance.Create();

        // Host grafı + fiş (TenantId=null) — tenant değişmeden.
        var hostVoucherId = await WithUnitOfWorkAsync(async () =>
        {
            var graph = await _seeder.SeedCompanyGraphAsync($"H{suffix}");
            return (await _vouchers.InsertAsync(NewVoucher(graph), autoSave: true)).Id;
        });

        Guid companyA, companyB, voucherA, voucherB;
        using (_currentTenant.Change(tenantId))
        {
            (companyA, companyB, voucherA, voucherB) = await WithUnitOfWorkAsync(async () =>
            {
                var graphA = await _seeder.SeedCompanyGraphAsync($"A{suffix}");
                var graphB = await _seeder.SeedCompanyGraphAsync($"B{suffix}");
                var a = await _vouchers.InsertAsync(NewVoucher(graphA), autoSave: true);
                var b = await _vouchers.InsertAsync(NewVoucher(graphB), autoSave: true);
                return (graphA.CompanyId, graphB.CompanyId, a.Id, b.Id);
            });
        }

        var allIds = new List<Guid> { hostVoucherId, voucherA, voucherB };

        using (_currentTenant.Change(tenantId))
        {
            // ① Working = A: kendi fişi görünür, YABANCI (B) yapısal görünmez; host tenant-filtreli → görünmez.
            _companyContext.CompanyId = companyA;
            var mine = await QueryIdsAsync(_vouchers, allIds);
            mine.ShouldContain(voucherA);
            mine.ShouldNotContain(voucherB);
            mine.ShouldNotContain(hostVoucherId);

            // ② Host fişi: tenant filtresi kapalıyken company'den de muaf; ama yabancı ŞİRKETİ açmaz.
            using (_dataFilter.Disable<IMultiTenant>())
            {
                var withHost = await QueryIdsAsync(_vouchers, allIds);
                withHost.ShouldContain(hostVoucherId);
                withHost.ShouldContain(voucherA);
                withHost.ShouldNotContain(voucherB);
            }

            // ③ Konsolide (working şirket yok): tenant'ın TÜM şirketleri görünür.
            _companyContext.CompanyId = null;
            var all = await QueryIdsAsync(_vouchers, allIds);
            all.ShouldContain(voucherA);
            all.ShouldContain(voucherB);
        }
    }

    [Fact]
    public async Task Ledger_owned_filter_isolates_companies()
    {
        await AssertIdOnlyOwnedFilterAsync(_ledger, MakeLedgerEntry);
    }

    [Fact]
    public async Task Snapshot_owned_filter_isolates_companies()
    {
        await AssertIdOnlyOwnedFilterAsync(_snapshots, MakeSnapshot);
    }

    // ── ortak senaryo (yalnız id-only entity'ler: ledger/snapshot — FK YOK, keyfi CompanyId eklenebilir) ──

    /// <summary>FK taşımayan bir ICompanyOwned entity için üç semantiği doğrular: (1) working şirket doluyken
    /// yalnız kendi kaydı görünür, yabancı GÖRÜNMEZ (güvenlik sınırı — holding-host null kolu YOK); (2) host
    /// kaydı (TenantId=null) tenant filtresi kapalıyken company'den muaf; (3) konsolide (context yok) PERMISSIVE.</summary>
    private async Task AssertIdOnlyOwnedFilterAsync<TEntity>(IRepository<TEntity, Guid> repository, Func<Guid, TEntity> makeForCompany)
        where TEntity : class, IEntity<Guid>
    {
        _companyContext.CompanyId = null; // seed sırasında context yok

        var tenantId = SimpleGuidGenerator.Instance.Create();
        var companyA = SimpleGuidGenerator.Instance.Create();
        var companyB = SimpleGuidGenerator.Instance.Create();

        // Host global kayıt (TenantId=null); CompanyId rastgele (working şirketle uyuşmaz → muafiyet host'tan gelmeli).
        var idHost = await WithUnitOfWorkAsync(async () =>
            (await repository.InsertAsync(makeForCompany(SimpleGuidGenerator.Instance.Create()), autoSave: true)).Id);

        Guid idA, idB;
        using (_currentTenant.Change(tenantId))
        {
            (idA, idB) = await WithUnitOfWorkAsync(async () =>
            {
                var a = await repository.InsertAsync(makeForCompany(companyA), autoSave: true);
                var b = await repository.InsertAsync(makeForCompany(companyB), autoSave: true);
                return (a.Id, b.Id);
            });
        }

        var allIds = new List<Guid> { idHost, idA, idB };

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = companyA;
            var mine = await QueryIdsAsync(repository, allIds);
            mine.ShouldContain(idA);
            mine.ShouldNotContain(idB);
            mine.ShouldNotContain(idHost);

            using (_dataFilter.Disable<IMultiTenant>())
            {
                var withHost = await QueryIdsAsync(repository, allIds);
                withHost.ShouldContain(idHost);
                withHost.ShouldContain(idA);
                withHost.ShouldNotContain(idB);
            }

            _companyContext.CompanyId = null;
            var all = await QueryIdsAsync(repository, allIds);
            all.ShouldContain(idA);
            all.ShouldContain(idB);
        }
    }

    /// <summary>Verilen id kümesinden aktif filtrelerle görünenlerin id listesi.</summary>
    private Task<List<Guid>> QueryIdsAsync<TEntity>(IRepository<TEntity, Guid> repository, List<Guid> ids)
        where TEntity : class, IEntity<Guid>
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var rows = await repository.GetListAsync(e => ids.Contains(e.Id));
            return rows.Select(e => e.Id).ToList();
        });
    }

    // ── entity fabrikaları (yalnız CompanyId/TenantId anlamlı; kalanlar minimal geçerli) ──────────

    private static Voucher NewVoucher(VoucherTestData graph)
    {
        return new Voucher(
            graph.CompanyId,
            graph.BranchId,
            graph.VaultId,
            graph.AccountId,
            graph.SubAccountId,
            voucherNumber: 1,
            voucherDate: DateTime.UtcNow);
    }

    private static BalanceLedgerEntry MakeLedgerEntry(Guid companyId)
    {
        // Voucher/VoucherLine transient — ledger ctor yalnız skaler değerleri kopyalar (nav YOK, insert edilmez).
        var voucher = new Voucher(
            companyId,
            SimpleGuidGenerator.Instance.Create(),
            vaultId: null,
            SimpleGuidGenerator.Instance.Create(),
            subAccountId: null,
            voucherNumber: 1,
            voucherDate: DateTime.UtcNow);
        var line = voucher.AddLine(SimpleGuidGenerator.Instance.Create(), MinimalLineInput());
        return new BalanceLedgerEntry(
            SimpleGuidGenerator.Instance.Create(),
            voucher,
            line,
            SimpleGuidGenerator.Instance.Create(),   // unitId
            amount: 100m);
    }

    private static BalanceSheetSnapshot MakeSnapshot(Guid companyId)
    {
        return new BalanceSheetSnapshot(
            BalanceSheetScope.Company,
            companyId,
            branchId: null,
            asOfDate: DateTime.Today,
            category: BalanceSheetCategory.AccountBalance,
            unitId: SimpleGuidGenerator.Instance.Create(),
            amount: 100m,
            valuationRate: 1m,
            net: 100m,
            baseUnitId: SimpleGuidGenerator.Instance.Create(),
            baseCurrencyCode: CurrencyUnitCode.TRY);
    }

    private static VoucherLineInput MinimalLineInput()
    {
        return new VoucherLineInput(
            ProcessType.Cash,
            ProcessDirectionType.Inbound,
            ProcessPaymentType.Normal,
            CommodityId: null,
            CommodityCode: CurrencyUnitCode.TRY,
            Quantity: 0m,
            Amount: 0m,
            Factor: 1m,
            Total: 0m,
            MainUnitId: SimpleGuidGenerator.Instance.Create(),
            PayFactor: 0m,
            MarketPrice: 0m,
            PayTotal: 0m,
            Profit: 0m,
            PayCommodityId: null,
            PayCommodityCode: null,
            PayUnitId: null,
            PayUnitRate: 0m,
            DueDate: null,
            Description: null);
    }
}
