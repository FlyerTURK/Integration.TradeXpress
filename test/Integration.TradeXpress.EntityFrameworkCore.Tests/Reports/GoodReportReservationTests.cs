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
/// <b>GOOD RAPORUNDA REZERVASYON</b> — <c>MetalReportStockTests</c>'in aynası (2026-08-05).
///
/// <para><b>Neden eklendi:</b> Good raporu "Rezervasyon YOK" varsayımıyla yazılmıştı — hem
/// <c>GetStockAsync</c>'te sayaçlar yoktu hem <c>GetMovementsAsync</c> tüm bacakları kümülatife katıyordu.
/// Sipariş rezervasyonu Good ürünlerini de kapsayacağı için bu varsayım, elimizde olmayan malı VARMIŞ
/// gösteren sessiz bir hataya dönüşecekti.</para>
///
/// <para>Metal tarafında aynı davranış zaten pin'liydi; bu testler iki raporun AYNI kuralı uyguladığını
/// kilitler — ortak aritmetik (<c>ReservationSplit</c>) ileride birinden koparsa buradan görülür.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class GoodReportReservationTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IGoodReportAppService _goodReport;
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public GoodReportReservationTests()
    {
        _goodReport        = GetRequiredService<IGoodReportAppService>();
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
    }

    /// <summary>ASIL KURAL: rezervasyon fiziksel Net'e GİRMEZ, ayrı sayaçta toplanır, kullanılabilirden düşer.</summary>
    [Fact]
    public async Task GetStock_excludes_reservation_from_net_and_computes_reserved_and_available()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("GRR"));
        _companyContext.CompanyId = data.CompanyId;

        // Fiziksel: Giriş 10 adet, Çıkış 2 adet → Net 8.
        await _voucherAppService.SaveLineAsync(GoodLine(data, ProcessDirectionType.Inbound,
            ProcessPaymentType.Normal, quantity: 10m));
        await _voucherAppService.SaveLineAsync(GoodLine(data, ProcessDirectionType.Outbound,
            ProcessPaymentType.Normal, quantity: 2m));

        // Rezervasyon: Çıkış 3 (müşteriye ayrılan) + Giriş 50 (tedarikçiden beklenen) → İKİSİ DE Net'e GİRMEZ.
        await _voucherAppService.SaveLineAsync(GoodLine(data, ProcessDirectionType.Outbound,
            ProcessPaymentType.Reservation, quantity: 3m));
        await _voucherAppService.SaveLineAsync(GoodLine(data, ProcessDirectionType.Inbound,
            ProcessPaymentType.Reservation, quantity: 50m));

        var rows = await _goodReport.GetStockAsync(new GoodReportFilterDto { BranchId = data.BranchId });
        var row = rows.ShouldHaveSingleItem();

        row.InQuantity.ShouldBe(10m);
        row.OutQuantity.ShouldBe(2m);
        row.NetQuantity.ShouldBe(8m);          // rezervasyon Net'i DEĞİŞTİRMEDİ

        row.ReservedOutQuantity.ShouldBe(3m);
        row.ReservedInQuantity.ShouldBe(50m);

        // Kullanılabilir = Net − RezerveÇıkış. RezerveGiriş EKLENMEZ (elimizde olmayan mal).
        row.AvailableQuantity.ShouldBe(5m);
    }

    /// <summary>Rezervasyon satırı listede GÖRÜNÜR ama yürüyen bakiyeyi HAREKET ETTİRMEZ.
    /// <para>Eski kod tüm bacakları topluyordu — rezervasyon bakiyeyi şişirir ve rapor yalan söylerdi.</para></summary>
    [Fact]
    public async Task GetMovements_shows_reservation_row_but_keeps_physical_running_balance()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("GRM"));
        _companyContext.CompanyId = data.CompanyId;

        await _voucherAppService.SaveLineAsync(GoodLine(data, ProcessDirectionType.Inbound,
            ProcessPaymentType.Normal, quantity: 10m));
        await _voucherAppService.SaveLineAsync(GoodLine(data, ProcessDirectionType.Outbound,
            ProcessPaymentType.Reservation, quantity: 4m));

        var rows = await _goodReport.GetMovementsAsync(new GoodReportFilterDto
        {
            BranchId = data.BranchId,
            Start    = DateTime.Today.AddDays(-1),
            End      = DateTime.Today.AddDays(1),
        });

        var reservationRow = rows.Single(r => r.IsReservation);
        reservationRow.Effect.ShouldBe(-4m);              // bilgi amaçlı çıkış gösterimi

        // ASIL KURAL: bakiye fiziksel kalır — 10, 6 DEĞİL.
        reservationRow.RunningBalance.ShouldBe(10m);
    }

    private static VoucherLineDto GoodLine(
        VoucherTestData data, ProcessDirectionType direction, ProcessPaymentType paymentType, decimal quantity)
    {
        return new VoucherLineDto
        {
            BranchId      = data.BranchId,
            VaultId       = data.VaultId,
            AccountId     = data.AccountId,
            SubAccountId  = data.SubAccountId,
            Type          = ProcessType.Good,
            Direction     = direction,
            PaymentType   = paymentType,
            CommodityCode = "TICARI",
            Quantity      = quantity,
            Amount        = quantity,
            Factor        = 1m,
            Total         = quantity,
            // MainUnitId BİLEREK boş — mamül satırında birim mamül seviyesindedir (Good.StockUnitCode),
            // satırda taşınmaz. Maden satırından ayrıldığı nokta budur.
        };
    }
}
