using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Nakit HAREKET raporu DEVREDEN pini (K4 SQL-side aggregation refactor'unun güvenlik ağı):
/// <see cref="ICashReportAppService.GetMovementsAsync"/> devreden'i (dönem başlangıcından önceki birikmiş net) artık
/// tüm satırları belleğe çekmeden SQL-side GROUP BY + SUM ile hesaplar. Dönem HAREKET satırları hâlâ satır-satır DETAY.
/// Gerçek EF (SQLite) üzerinde çalışır → devreden net'in ELLE hesaplı beklenenle BİREBİR aynı kaldığını ve yürüyen
/// bakiyenin devreden üstünden devam ettiğini kanıtlar.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class CashReportMovementsCarryTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ICashReportAppService _cash;
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public CashReportMovementsCarryTests()
    {
        _cash              = GetRequiredService<ICashReportAppService>();
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task GetMovements_carry_forward_is_sql_aggregated_net_before_period_start()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());
        _companyContext.CompanyId = data.CompanyId;

        // Dönem ÖNCESİ (devreden'e girmeli): Giriş 1000 (10 Oca), Çıkış 300 (15 Oca) → net 700.
        await SaveCashOnDateAsync(data, ProcessDirectionType.Inbound, 1000m, new DateTime(2026, 1, 10));
        await SaveCashOnDateAsync(data, ProcessDirectionType.Outbound, 300m, new DateTime(2026, 1, 15));

        // Dönem İÇİ (detay satır): Giriş 500 (5 Şub).
        await SaveCashOnDateAsync(data, ProcessDirectionType.Inbound, 500m, new DateTime(2026, 2, 5));

        var rows = await _cash.GetMovementsAsync(new CashReportFilterDto
        {
            BranchId = data.BranchId,
            Start    = new DateTime(2026, 2, 1),
            End      = new DateTime(2026, 2, 28),
        });

        // Devreden satırı: SQL-agregeli net 700 (1000 − 300), yürüyen bakiye 700.
        var carry = rows.Single(r => r.IsCarryForward);
        carry.UnitId.ShouldBe(data.TryUnitId);
        carry.Source.ShouldBe("Devreden");
        carry.CashAmount.ShouldBe(700m);
        carry.RunningBalance.ShouldBe(700m);

        // Dönem hareketi (detay): +500, yürüyen bakiye devreden ÜSTÜNE = 1200.
        var period = rows.Single(r => !r.IsCarryForward);
        period.CashAmount.ShouldBe(500m);
        period.RunningBalance.ShouldBe(1200m);
    }

    private Task SaveCashOnDateAsync(VoucherTestData data, ProcessDirectionType direction, decimal amount, DateTime date)
    {
        var line = VoucherTestLines.CashLine(data, direction, amount);
        line.VoucherDate = date;
        return _voucherAppService.SaveLineAsync(line);
    }
}
