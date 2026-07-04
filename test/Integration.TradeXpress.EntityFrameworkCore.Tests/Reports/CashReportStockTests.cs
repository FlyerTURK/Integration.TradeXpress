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
/// Nakit STOK raporu davranış pini (K4 SQL-side aggregation refactor'unun güvenlik ağı): <see cref="ICashReportAppService.GetStockAsync"/>
/// artık satırları belleğe çekmeden SQL-side GROUP BY + SUM ile Giren/Çıkan/Net üretir. Bu test gerçek EF (SQLite)
/// üzerinde çalışır → hem sorgunun ÇEVRİLDİĞİNİ (translation) hem de iki-bacak/işaret/birim-gruplama sonucunun
/// ELLE hesaplı beklenenle BİREBİR aynı kaldığını kanıtlar (in-memory desenin karakterizasyonu, sonuç değişmez).
/// <list type="bullet">
///   <item>Sol bacak (Cash process, MainUnit): Giriş +Total / Çıkış −Total.</item>
///   <item>Sağ bacak (WithCash, PayUnit): işaret tersi — Giriş −PayTotal / Çıkış +PayTotal.</item>
///   <item>Çok-birim gruplama: farklı MainUnit ayrı satır.</item>
/// </list>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class CashReportStockTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ICashReportAppService _cash;
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public CashReportStockTests()
    {
        _cash              = GetRequiredService<ICashReportAppService>();
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task GetStock_aggregates_both_legs_and_groups_by_unit_with_manual_expected_totals()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());
        _companyContext.CompanyId = data.CompanyId;

        // TRY sol bacak: Cash Giriş 1000 (+), Cash Çıkış 300 (−).
        await _voucherAppService.SaveLineAsync(VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 1000m));
        await _voucherAppService.SaveLineAsync(VoucherTestLines.CashLine(data, ProcessDirectionType.Outbound, 300m));

        // TRY iki bacak birden: Cash Giriş 500 PEŞİN → sol bacak +500, sağ bacak −500 (aynı birim TRY).
        await _voucherAppService.SaveLineAsync(
            VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 500m, ProcessPaymentType.WithCash));

        // HAS sol bacak (çok-birim gruplama): Cash Giriş 20, ana birim HAS'a çekilir.
        var hasCash = VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 20m);
        hasCash.MainUnitId    = data.HasUnitId;
        hasCash.CommodityCode = CurrencyUnitCode.HAS;
        await _voucherAppService.SaveLineAsync(hasCash);

        var rows = await _cash.GetStockAsync(new CashReportFilterDto { BranchId = data.BranchId });

        rows.Count.ShouldBe(2);

        // TRY: Giren = 1000 + 500 = 1500; Çıkan = 300 + 500 = 800; Net = 700.
        var tryRow = rows.Single(r => r.UnitId == data.TryUnitId);
        tryRow.InTotal.ShouldBe(1500m);
        tryRow.OutTotal.ShouldBe(800m);
        tryRow.Net.ShouldBe(700m);

        // HAS: Giren 20, Çıkan 0, Net 20 (tek sol bacak).
        var hasRow = rows.Single(r => r.UnitId == data.HasUnitId);
        hasRow.InTotal.ShouldBe(20m);
        hasRow.OutTotal.ShouldBe(0m);
        hasRow.Net.ShouldBe(20m);

        // Sıralama UnitCode ARTAN: HAS < TRY.
        rows[0].UnitId.ShouldBe(data.HasUnitId);
        rows[1].UnitId.ShouldBe(data.TryUnitId);
    }
}
