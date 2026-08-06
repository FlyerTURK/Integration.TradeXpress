using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// <b>ÇİFT SAYIM TUZAĞI</b> — Faz 7'nin en kritik regresyon testi.
///
/// <para>Senaryo: 50 gr giriş → 20 gr rezerve (<c>Available=30</c>) → mal hazırlanıp 20 gr fiziki çıkış yapılır
/// <b>ve rezervasyon serbest bırakılır</b>. Doğru sonuç: <c>Net=30 · ReservedOut=0 · Available=30</c>.</para>
///
/// <para><b>Serbest bırakmayı unutmak bu testi kırar:</b> rezervasyon durursa <c>Available</c> 10 çıkar — aynı
/// 20 gram iki kez düşülmüş olur ve ürün stokta olmadığı hâlde satıştan kalkar. Hata sessizdir: hiçbir yerde
/// istisna doğmaz, yalnız kanal adedi sebepsizce küçülür.</para>
///
/// <para>Test rezervasyon ARİTMETİĞİNİ fiş seviyesinde sürer (rezervasyon fişi = <c>PaymentType.Reservation</c>
/// satırı; serbest bırakma = o satırın soft-delete'i). Sipariş orkestrasyonundan bağımsızdır — kırılırsa
/// sebebi doğrudan görülür.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class OrderReservationDoubleCountTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IMetalReportAppService _metalReport;
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public OrderReservationDoubleCountTests()
    {
        _metalReport       = GetRequiredService<IMetalReportAppService>();
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task Reservation_released_after_physical_exit_does_not_double_count()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("RDC"));
        _companyContext.CompanyId = data.CompanyId;

        // 1) Alış: 50 gr giriş.
        await _voucherAppService.SaveLineAsync(MetalLine(
            data, ProcessDirectionType.Inbound, ProcessPaymentType.Normal, amount: 50m, quantity: 5m));

        // 2) Sipariş geldi → 20 gr REZERVE (fiziksel Net'e girmez).
        var reservation = await _voucherAppService.SaveLineAsync(MetalLine(
            data, ProcessDirectionType.Outbound, ProcessPaymentType.Reservation, amount: 20m, quantity: 2m));

        var afterReserve = await StockAsync(data);
        afterReserve.NetAmount.ShouldBe(50m);            // rezervasyon Net'i DEĞİŞTİRMEDİ
        afterReserve.ReservedOutAmount.ShouldBe(20m);
        afterReserve.AvailableAmount.ShouldBe(30m);      // 50 − 20

        // 3) Mal hazırlandı → 20 gr FİZİKİ ÇIKIŞ.
        await _voucherAppService.SaveLineAsync(MetalLine(
            data, ProcessDirectionType.Outbound, ProcessPaymentType.Normal, amount: 20m, quantity: 2m));

        // 4) ...ve rezervasyon SERBEST BIRAKILIR (satır soft-delete). Bu adım unutulursa çift sayım doğar.
        await _voucherAppService.DeleteLineAsync(
            reservation.VoucherId!.Value, reservation.Id, "Fiziki çıkış yapıldı — rezervasyon serbest.");

        var afterExit = await StockAsync(data);
        afterExit.NetAmount.ShouldBe(30m);               // 50 − 20 fiziki çıkış
        afterExit.ReservedOutAmount.ShouldBe(0m);        // rezervasyon düştü
        afterExit.AvailableAmount.ShouldBe(30m);         // ⚠ 10 DEĞİL — çift sayım tuzağının pini
    }

    /// <summary>Serbest bırakma UNUTULURSA ne olduğunu da kilitler: aynı 20 gram İKİ KEZ düşer.
    /// <para>Bu testin varlık sebebi, yukarıdaki testin neyi koruduğunu belgelemek — birisi serbest bırakma
    /// adımını "gereksiz" diye kaldırırsa iki test birden kırılır ve sebebi görünür olur.</para></summary>
    [Fact]
    public async Task Forgetting_to_release_double_counts_the_same_amount()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("RDF"));
        _companyContext.CompanyId = data.CompanyId;

        await _voucherAppService.SaveLineAsync(MetalLine(
            data, ProcessDirectionType.Inbound, ProcessPaymentType.Normal, amount: 50m, quantity: 5m));
        await _voucherAppService.SaveLineAsync(MetalLine(
            data, ProcessDirectionType.Outbound, ProcessPaymentType.Reservation, amount: 20m, quantity: 2m));
        await _voucherAppService.SaveLineAsync(MetalLine(
            data, ProcessDirectionType.Outbound, ProcessPaymentType.Normal, amount: 20m, quantity: 2m));

        var row = await StockAsync(data);
        row.NetAmount.ShouldBe(30m);
        row.ReservedOutAmount.ShouldBe(20m);            // rezervasyon HÂLÂ duruyor
        row.AvailableAmount.ShouldBe(10m);              // 30 − 20: aynı mal iki kez düşülmüş
    }

    private async Task<MetalStockRowDto> StockAsync(VoucherTestData data)
    {
        var rows = await _metalReport.GetStockAsync(new MetalReportFilterDto { BranchId = data.BranchId });
        return rows.Single(r => r.UnitId == data.HasUnitId);
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
