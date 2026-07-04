using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Hurda STOK raporu davranış pini (K4 SQL-side aggregation refactor'unun güvenlik ağı):
/// <see cref="IScrapReportAppService.GetStockAsync"/> artık satırları belleğe çekmeden SQL-side GROUP BY + SUM ile
/// Giren/Çıkan/Net üretir. Gerçek EF (SQLite) üzerinde çalışır → hem SQL ÇEVRİLDİĞİNİ hem de bacak/işaret/birim-gruplama
/// sonucunun ELLE hesaplı beklenenle BİREBİR aynı kaldığını kanıtlar:
/// <list type="bullet">
///   <item>Ana bacak (Normal, MainUnit): Giriş +Amount / Çıkış −Amount.</item>
///   <item>Peşin (WithCash): bakiyeye YANSIMAZ (leg üretilmez).</item>
///   <item>Bedelli (WithCurrency, PayUnit): PayTotal @ PayUnit — ayrı birim satırı.</item>
/// </list>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class ScrapReportStockTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IScrapReportAppService _scrap;
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public ScrapReportStockTests()
    {
        _scrap             = GetRequiredService<IScrapReportAppService>();
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task GetStock_aggregates_main_and_bedelli_legs_excludes_pesin_and_groups_by_unit()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());
        _companyContext.CompanyId = data.CompanyId;

        // Ana bacak HAS: Normal Giriş 10 (+), Normal Çıkış 4 (−).
        await _voucherAppService.SaveLineAsync(ScrapLine(data, ProcessDirectionType.Inbound, ProcessPaymentType.Normal,
            mainUnitId: data.HasUnitId, amount: 10m));
        await _voucherAppService.SaveLineAsync(ScrapLine(data, ProcessDirectionType.Outbound, ProcessPaymentType.Normal,
            mainUnitId: data.HasUnitId, amount: 4m));

        // Peşin (WithCash): bakiyeye/stoka YANSIMAMALI — hiçbir birime katkı yapmaz.
        await _voucherAppService.SaveLineAsync(ScrapLine(data, ProcessDirectionType.Inbound, ProcessPaymentType.WithCash,
            mainUnitId: data.HasUnitId, amount: 7m));

        // Bedelli (WithCurrency): PayUnit=TRY, PayTotal=500 Giriş (+) → ayrı TRY satırı.
        await _voucherAppService.SaveLineAsync(ScrapLine(data, ProcessDirectionType.Inbound, ProcessPaymentType.WithCurrency,
            mainUnitId: data.HasUnitId, amount: 0m, payUnitId: data.TryUnitId, payTotal: 500m));

        var rows = await _scrap.GetStockAsync(new ScrapReportFilterDto { BranchId = data.BranchId });

        rows.Count.ShouldBe(2);

        // HAS ana bacak: Giren 10, Çıkan 4, Net 6 (Peşin 7 dahil DEĞİL).
        var hasRow = rows.Single(r => r.UnitId == data.HasUnitId);
        hasRow.InTotal.ShouldBe(10m);
        hasRow.OutTotal.ShouldBe(4m);
        hasRow.Net.ShouldBe(6m);

        // TRY Bedelli bacak: Giren 500, Çıkan 0, Net 500.
        var tryRow = rows.Single(r => r.UnitId == data.TryUnitId);
        tryRow.InTotal.ShouldBe(500m);
        tryRow.OutTotal.ShouldBe(0m);
        tryRow.Net.ShouldBe(500m);

        // Sıralama UnitCode ARTAN: HAS < TRY.
        rows[0].UnitId.ShouldBe(data.HasUnitId);
        rows[1].UnitId.ShouldBe(data.TryUnitId);
    }

    private static VoucherLineDto ScrapLine(
        VoucherTestData data, ProcessDirectionType direction, ProcessPaymentType paymentType,
        Guid mainUnitId, decimal amount, Guid? payUnitId = null, decimal payTotal = 0m)
    {
        return new VoucherLineDto
        {
            BranchId      = data.BranchId,
            VaultId       = data.VaultId,
            AccountId     = data.AccountId,
            SubAccountId  = data.SubAccountId,
            Type          = ProcessType.Scrap,
            Direction     = direction,
            PaymentType   = paymentType,
            CommodityCode = CurrencyUnitCode.HAS,
            Amount        = amount,
            Factor        = 1m,
            Total         = amount,
            MainUnitId    = mainUnitId,
            PayUnitId     = payUnitId,
            PayTotal      = payTotal,
        };
    }
}
