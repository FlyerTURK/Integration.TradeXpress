using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// <see cref="VoucherLine"/> takoz kur/işçilik snapshot alanlarının N5 (decimal(18,5)) hassasiyetle
/// map'lendiğini doğrular. Bu alanlar önce HasPrecision'sız kalıp SQL Server'da default decimal(18,2)
/// alıyor, milyem/kur değerlerini sessizce N2'ye kırpıyordu (finansal doğruluk kaybı). İki koruma:
/// (1) EF model config'i alan-alan N5/N2 doğrular (provider-bağımsız — SQL Server kolon tipini yansıtır);
/// (2) 5-haneli değerlerin insert→read round-trip'te korunduğunu doğrular (entity kalıcılığı).
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class VoucherLinePrecisionTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly IDbContextProvider<TradeXpressDbContext> _dbContextProvider;
    private readonly IGuidGenerator _guidGenerator;

    public VoucherLinePrecisionTests()
    {
        _voucherRepository = GetRequiredService<IRepository<Voucher, Guid>>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _dbContextProvider = GetRequiredService<IDbContextProvider<TradeXpressDbContext>>();
        _guidGenerator     = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task Rate_snapshot_columns_are_mapped_as_decimal_18_5()
    {
        var entityType = await GetVoucherLineEntityTypeAsync();

        // 11 kur/işçilik snapshot alanı — hepsi N5 (kur/milyem hassasiyeti).
        string[] rateProperties =
        {
            nameof(VoucherLine.GoldRate),
            nameof(VoucherLine.SilverRate),
            nameof(VoucherLine.PlatinumRate),
            nameof(VoucherLine.PalladiumRate),
            nameof(VoucherLine.GoldLaborUnitRate),
            nameof(VoucherLine.SilverLaborUnitRate),
            nameof(VoucherLine.PlatinumLaborUnitRate),
            nameof(VoucherLine.PalladiumLaborUnitRate),
            nameof(VoucherLine.SilverLaborRate),
            nameof(VoucherLine.PlatinumLaborRate),
            nameof(VoucherLine.PalladiumLaborRate),
        };

        foreach (var name in rateProperties)
        {
            var property = entityType.FindProperty(name);
            property.ShouldNotBeNull();
            property!.GetPrecision().ShouldBe(VoucherConsts.FactorPrecision, $"{name} precision");
            property.GetScale().ShouldBe(VoucherConsts.FactorScale, $"{name} scale (N5)");
        }
    }

    [Fact]
    public async Task Assay_amount_column_is_mapped_as_decimal_18_2()
    {
        var entityType = await GetVoucherLineEntityTypeAsync();

        // Çeşni numune miktarı = gram ağırlık (Amount ailesi) → N2.
        var property = entityType.FindProperty(nameof(VoucherLine.AssayAmount));
        property.ShouldNotBeNull();
        property!.GetPrecision().ShouldBe(VoucherConsts.AmountPrecision);
        property.GetScale().ShouldBe(VoucherConsts.AmountScale);
    }

    [Fact]
    public async Task Five_decimal_rate_snapshots_survive_insert_and_read()
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync());

        // 5-haneli değerler; bazıları N2'ye kırpılsaydı BOZULURDU (0.00815→0.01, 0.00025→0.00) —
        // round-trip'te AYNEN korunmalı.
        var input = new VoucherLineInput(
            Type:        ProcessType.Bullion,
            Direction:   ProcessDirectionType.Inbound,
            PaymentType: null,
            CommodityId: null,
            CommodityCode: "BULLION",
            Quantity:    0m,
            Amount:      100m,
            Factor:      0.91600m,
            Total:       0m,
            MainUnitId:  data.HasUnitId,
            PayFactor:   0m,
            MarketPrice: 0m,
            PayTotal:    0m,
            Profit:      0m,
            PayCommodityId:   null,
            PayCommodityCode: null,
            PayUnitId:   data.TryUnitId,
            PayUnitRate: 0m,
            DueDate:     null,
            Description: null,
            AssayAmount:            12.34m,
            SilverFactor:           0.04000m,
            GoldRate:               0.99915m,
            SilverRate:             0.12345m,
            PlatinumRate:           1.23456m,
            PalladiumRate:          0.54321m,
            GoldLaborUnitRate:      0.11111m,
            SilverLaborUnitRate:    0.22222m,
            PlatinumLaborUnitRate:  0.33333m,
            PalladiumLaborUnitRate: 0.44444m,
            SilverLaborRate:        0.00815m,
            PlatinumLaborRate:      0.00025m,
            PalladiumLaborRate:     0.98765m);

        var voucherId = await WithUnitOfWorkAsync(async () =>
        {
            var voucher = new Voucher(
                data.CompanyId, data.BranchId, data.VaultId,
                AccountType.CurrentAccount,
                data.AccountId, "ACC", data.SubAccountId, "SUB",
                voucherNumber: 1L, voucherDate: DateTime.Today);
            voucher.AddLine(_guidGenerator.Create(), input);
            await _voucherRepository.InsertAsync(voucher, autoSave: true);
            return voucher.Id;
        });

        var line = await WithUnitOfWorkAsync(async () =>
        {
            var query = await _voucherRepository.WithDetailsAsync(v => v.Lines);
            var voucher = query.Single(v => v.Id == voucherId);
            return voucher.Lines.Single();
        });

        line.GoldRate.ShouldBe(0.99915m);
        line.SilverRate.ShouldBe(0.12345m);
        line.PlatinumRate.ShouldBe(1.23456m);
        line.PalladiumRate.ShouldBe(0.54321m);
        line.GoldLaborUnitRate.ShouldBe(0.11111m);
        line.SilverLaborUnitRate.ShouldBe(0.22222m);
        line.PlatinumLaborUnitRate.ShouldBe(0.33333m);
        line.PalladiumLaborUnitRate.ShouldBe(0.44444m);
        line.SilverLaborRate.ShouldBe(0.00815m);
        line.PlatinumLaborRate.ShouldBe(0.00025m);
        line.PalladiumLaborRate.ShouldBe(0.98765m);
        line.AssayAmount.ShouldBe(12.34m);
    }

    private async Task<IEntityType> GetVoucherLineEntityTypeAsync()
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var entityType = dbContext.Model.FindEntityType(typeof(VoucherLine));
            entityType.ShouldNotBeNull();
            return entityType!;
        });
    }
}
