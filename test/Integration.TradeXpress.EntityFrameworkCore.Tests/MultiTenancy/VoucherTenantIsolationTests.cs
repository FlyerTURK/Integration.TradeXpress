using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Vouchers.Balance;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiTenancy;

/// <summary>
/// Multi-tenant izolasyon (E-5/D): Tenant A'da yazılan fiş + ledger, tenant B altında hiçbir
/// okuma yolundan (GetList / GetBalances / ledger sorgusu) görünmez — IMultiTenant filtresi
/// company-scope'tan bağımsız ikinci savunma hattıdır (13 canlı tenant!).
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class VoucherTenantIsolationTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IVoucherAppService _voucherAppService;
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledgerRepository;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ICurrentTenant _currentTenant;

    public VoucherTenantIsolationTests()
    {
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _ledgerRepository  = GetRequiredService<IRepository<BalanceLedgerEntry, Guid>>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
        _currentTenant     = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Tenant_B_cannot_see_tenant_A_vouchers_or_ledger()
    {
        // Guid.NewGuid yasak (BannedSymbols) → SimpleGuidGenerator (DI'sız test akışı).
        var tenantA = SimpleGuidGenerator.Instance.Create();
        var tenantB = SimpleGuidGenerator.Instance.Create();

        VoucherTestData data;
        Guid voucherId;

        // ── Tenant A: org grafı + fiş (+1000 TRY) + ledger ──
        using (_currentTenant.Change(tenantA))
        {
            data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());
            _companyContext.CompanyId = data.CompanyId;

            var line = await _voucherAppService.SaveLineAsync(
                VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 1000m));
            voucherId = line.VoucherId!.Value;

            // Kontrol grubu: A kendi verisini görür.
            var ownList = await _voucherAppService.GetListAsync(
                new VoucherListRequestDto { SubAccountId = data.SubAccountId });
            ownList.TotalCount.ShouldBe(1);

            (await GetLedgerCountAsync(voucherId)).ShouldBe(1);

            var ownBalance = await _voucherAppService.GetBalancesAsync(data.SubAccountId);
            ownBalance.Lines.Single(l => l.UnitId == data.TryUnitId).Net.ShouldBe(1000m);
        }

        // ── Tenant B: aynı company/subaccount id'leriyle (sızıntı denemesi) HİÇBİR ŞEY görünmez ──
        using (_currentTenant.Change(tenantB))
        {
            // Working company kasıtlı olarak A'nın şirketi bırakılır — tenant filtresi tek başına kesmeli.
            _companyContext.CompanyId = data.CompanyId;

            var list = await _voucherAppService.GetListAsync(
                new VoucherListRequestDto { SubAccountId = data.SubAccountId });
            list.TotalCount.ShouldBe(0);
            list.Items.ShouldBeEmpty();

            var balances = await _voucherAppService.GetBalancesAsync(data.SubAccountId);
            balances.Lines.Where(l => l.Net != 0m).ShouldBeEmpty();

            // Ledger sorgusu da izole: B altında A'nın ledger satırı dönmez.
            (await GetLedgerCountAsync(voucherId)).ShouldBe(0);
        }

        // ── Tenant A'ya dönüş: veri bozulmadan durur (B'deki sorgular yan etkisiz) ──
        using (_currentTenant.Change(tenantA))
        {
            _companyContext.CompanyId = data.CompanyId;
            (await GetLedgerCountAsync(voucherId)).ShouldBe(1);

            var list = await _voucherAppService.GetListAsync(
                new VoucherListRequestDto { SubAccountId = data.SubAccountId });
            list.TotalCount.ShouldBe(1);
        }
    }

    private Task<long> GetLedgerCountAsync(Guid voucherId)
    {
        return WithUnitOfWorkAsync(() => _ledgerRepository.LongCountAsync(e => e.VoucherId == voucherId));
    }
}
