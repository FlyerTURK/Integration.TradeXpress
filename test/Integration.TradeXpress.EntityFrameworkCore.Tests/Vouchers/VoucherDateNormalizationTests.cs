using System;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vouchers.Balance;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Golden: fiş tarihinin (VoucherDate) WALL-CLOCK korunduğu — kayıt→okuma turunda gün/saat KAYMAZ.
/// <para><b>Bağlam:</b> AbpClockOptions.Kind=Utc, DateTime.Now (Kind=Local) save'te UTC'ye normalize olurdu
/// (Türkiye −3s) → gece-yarısına yakın tarihler bir önceki güne kayardı. Düzeltme: <c>Voucher.VoucherDate</c>
/// ve <c>BalanceLedgerEntry.VoucherDate</c> alanları <c>[DisableDateTimeNormalization]</c> + entity ctor
/// Kind'ı <see cref="DateTimeKind.Unspecified"/>'e sabitler. Bu test o garantiyi pinler (yalnız VoucherDate; Faz-1).</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class VoucherDateNormalizationTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledgerRepository;
    private readonly IVoucherAppService _voucherAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public VoucherDateNormalizationTests()
    {
        _voucherRepository = GetRequiredService<IRepository<Voucher, Guid>>();
        _ledgerRepository  = GetRequiredService<IRepository<BalanceLedgerEntry, Guid>>();
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task VoucherDate_near_midnight_wall_clock_survives_save_and_read_without_day_shift()
    {
        var data = await ArrangeCompanyAsync();

        // Gece-yarısına yakın 00:30:45 — UTC normalizasyonu OLSAYDI Türkiye'de −3s ile bir ÖNCEKİ güne
        // (14 Mart 21:30) kayardı. Kind=Local (eski hatalı yol) verilir; düzeltme yine de gün+saati korumalı.
        var wallClock = new DateTime(2026, 3, 15, 0, 30, 45, DateTimeKind.Local);

        var voucherId = await WithUnitOfWorkAsync(async () =>
        {
            var voucher = await _voucherRepository.InsertAsync(
                new Voucher(
                    data.CompanyId,
                    data.BranchId,
                    data.VaultId,
                    data.AccountId,
                    data.SubAccountId,
                    voucherNumber: 501,
                    voucherDate: wallClock,
                    description: "gece-yarisi golden"),
                autoSave: true);
            return voucher.Id;
        });

        var read = await WithUnitOfWorkAsync(() => _voucherRepository.GetAsync(voucherId));

        // Gün KORUNUR (bir önceki güne kaymaz) + saat/dakika/saniye aynen — wall-clock tam korunur.
        read.VoucherDate.ShouldBe(new DateTime(2026, 3, 15, 0, 30, 45));
    }

    [Fact]
    public async Task Ledger_VoucherDate_copies_voucher_wall_clock_without_day_shift()
    {
        var data = await ArrangeCompanyAsync();

        // AppService yolu: satır kaydı fiş açar (VoucherDate DTO'dan gelir) + poster ledger yazar.
        // Ledger.VoucherDate = Voucher.VoucherDate kopyası; ikisi de wall-clock/kaymasız olmalı.
        var wallClock = new DateTime(2026, 3, 15, 0, 45, 0, DateTimeKind.Local);
        var dto = VoucherTestLines.CashLine(data, ProcessDirectionType.Inbound, 1000m);
        dto.VoucherDate = wallClock;

        var saved = await _voucherAppService.SaveLineAsync(dto);

        var voucher = await WithUnitOfWorkAsync(() => _voucherRepository.GetAsync(saved.VoucherId!.Value));
        voucher.VoucherDate.ShouldBe(new DateTime(2026, 3, 15, 0, 45, 0));

        var ledger = await WithUnitOfWorkAsync(
            () => _ledgerRepository.GetListAsync(e => e.VoucherId == saved.VoucherId!.Value));
        ledger.ShouldHaveSingleItem().VoucherDate.ShouldBe(new DateTime(2026, 3, 15, 0, 45, 0));
    }

    private async Task<VoucherTestData> ArrangeCompanyAsync()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());
        _companyContext.CompanyId = data.CompanyId;
        return data;
    }
}
