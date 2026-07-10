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
/// Maden STOK raporu — Rezervasyon (Muadil M0) davranış pini:
/// <see cref="IMetalReportAppService.GetStockAsync"/> fiziksel Net'e Rezervasyon satırlarını DAHİL ETMEZ;
/// rezervasyonlar ayrı sayaçlarda toplanır ve Kullanılabilir = Net − RezerveÇıkış hesaplanır:
/// <list type="bullet">
///   <item>Fiziksel bacaklar (Normal/Peşin dahil): Giriş +Amount/+Quantity, Çıkış −.</item>
///   <item>Rezervasyon ÇIKIŞ: müşteriye ayrılan → ReservedOut*, kullanılabilirden düşer.</item>
///   <item>Rezervasyon GİRİŞ: tedarikçiden beklenen → ReservedIn* (bilgi amaçlı; kullanılabilire eklenmez).</item>
/// </list>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class MetalReportStockTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IMetalReportAppService _metalReport;
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public MetalReportStockTests()
    {
        _metalReport       = GetRequiredService<IMetalReportAppService>();
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task GetStock_excludes_reservation_from_net_and_computes_reserved_and_available()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());
        _companyContext.CompanyId = data.CompanyId;

        // Fiziksel hareketler: Normal Giriş 50 gr / 10 adet (+), Normal Çıkış 10 gr / 2 adet (−),
        // Peşin Giriş 5 gr / 1 adet (+ — peşin FİZİKSEL harekettir, stok Net'ine girer).
        await _voucherAppService.SaveLineAsync(MetalLine(data, ProcessDirectionType.Inbound,
            ProcessPaymentType.Normal, amount: 50m, quantity: 10m));
        await _voucherAppService.SaveLineAsync(MetalLine(data, ProcessDirectionType.Outbound,
            ProcessPaymentType.Normal, amount: 10m, quantity: 2m));
        await _voucherAppService.SaveLineAsync(MetalLine(data, ProcessDirectionType.Inbound,
            ProcessPaymentType.WithCash, amount: 5m, quantity: 1m));

        // Rezervasyon ÇIKIŞ 20 gr / 4 adet (müşteriye ayrılan) + Rezervasyon GİRİŞ 100 gr / 20 adet
        // (tedarikçiden beklenen) → İKİSİ DE fiziksel Net'e GİRMEZ.
        await _voucherAppService.SaveLineAsync(MetalLine(data, ProcessDirectionType.Outbound,
            ProcessPaymentType.Reservation, amount: 20m, quantity: 4m));
        await _voucherAppService.SaveLineAsync(MetalLine(data, ProcessDirectionType.Inbound,
            ProcessPaymentType.Reservation, amount: 100m, quantity: 20m));

        var rows = await _metalReport.GetStockAsync(new MetalReportFilterDto { BranchId = data.BranchId });

        var row = rows.Single(r => r.UnitId == data.HasUnitId);

        // Fiziksel: Giriş 55 (50 Normal + 5 Peşin), Çıkış 10, Net 45 — Rezervasyon 20/100 dahil DEĞİL.
        row.InAmount.ShouldBe(55m);
        row.OutAmount.ShouldBe(10m);
        row.NetAmount.ShouldBe(45m);
        row.InQuantity.ShouldBe(11m);
        row.OutQuantity.ShouldBe(2m);
        row.NetQuantity.ShouldBe(9m);

        // Rezervasyon sayaçları ayrı toplanır.
        row.ReservedOutAmount.ShouldBe(20m);
        row.ReservedOutQuantity.ShouldBe(4m);
        row.ReservedInAmount.ShouldBe(100m);
        row.ReservedInQuantity.ShouldBe(20m);

        // Kullanılabilir = Net − RezerveÇıkış (RezerveGiriş EKLENMEZ — ilk faz kararı).
        row.AvailableAmount.ShouldBe(25m);
        row.AvailableQuantity.ShouldBe(5m);
    }

    [Fact]
    public async Task GetMovements_shows_reservation_row_but_keeps_physical_running_balance()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("MRT"));
        _companyContext.CompanyId = data.CompanyId;

        await _voucherAppService.SaveLineAsync(MetalLine(data, ProcessDirectionType.Inbound,
            ProcessPaymentType.Normal, amount: 30m, quantity: 6m));
        await _voucherAppService.SaveLineAsync(MetalLine(data, ProcessDirectionType.Outbound,
            ProcessPaymentType.Reservation, amount: 12m, quantity: 2m));

        var rows = await _metalReport.GetMovementsAsync(new MetalReportFilterDto
        {
            BranchId = data.BranchId,
            Start    = DateTime.Today.AddDays(-1),
            End      = DateTime.Today.AddDays(1),
        });

        // Rezervasyon satırı listede GÖRÜNÜR (Source="Rezervasyon") ama Son Durum fiziksel kalır (30).
        var reservationRow = rows.Single(r => r.IsReservation);
        reservationRow.Source.ShouldBe("Rezervasyon");
        reservationRow.Effect.ShouldBe(-12m);           // bilgi amaçlı Çıkan gösterimi
        reservationRow.RunningBalance.ShouldBe(30m);    // fiziksel kümülatife KATILMAZ
        reservationRow.RunningQty.ShouldBe(6m);
        reservationRow.Devir.ShouldBe(30m);             // Devir de fiziksel bakiyeyi gösterir

        rows.Last().RunningBalance.ShouldBe(30m);
    }

    private static VoucherLineDto MetalLine(
        VoucherTestData data, ProcessDirectionType direction, ProcessPaymentType paymentType,
        decimal amount, decimal quantity)
    {
        return new VoucherLineDto
        {
            BranchId      = data.BranchId,
            VaultId       = data.VaultId,
            AccountId     = data.AccountId,
            SubAccountId  = data.SubAccountId,
            Type          = ProcessType.Metal,
            Direction     = direction,
            PaymentType   = paymentType,
            CommodityCode = CurrencyUnitCode.HAS,
            Quantity      = quantity,
            Amount        = amount,
            Factor        = 1m,
            Total         = amount,
            MainUnitId    = data.HasUnitId,
        };
    }
}
