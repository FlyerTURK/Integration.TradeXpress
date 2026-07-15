using System;
using System.Threading.Tasks;
using Integration.Framework.Data;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// VoucherNumber tahsis yarışının regresyon ağı: unique index (TenantId,CompanyId,VoucherNumber)
/// ihlalinde numara MAX+1 ile yeniden hesaplanıp insert ŞEFFAFÇA tekrarlanır; kalıcı çakışmada
/// (her deneme aynı numarayı üretir) denemeler tükenince lokalize NumberConflict'e çevrilir;
/// alakasız DbUpdateException'lar (ör. FK ihlali — Sqlite'ta primary kodu aynı 19!) ÇEVRİLMEZ.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class VoucherNumberAllocatorTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly VoucherNumberAllocator _allocator;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUniqueConstraintViolationDetector _detector;
    private readonly IRepository<Voucher, Guid> _voucherRepository;

    public VoucherNumberAllocatorTests()
    {
        _allocator         = GetRequiredService<VoucherNumberAllocator>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
        _currentTenant     = GetRequiredService<ICurrentTenant>();
        _detector          = GetRequiredService<IUniqueConstraintViolationDetector>();
        _voucherRepository = GetRequiredService<IRepository<Voucher, Guid>>();
    }

    [Fact]
    public async Task Stale_number_conflict_is_retried_transparently_with_recomputed_number()
    {
        // Sqlite unique index'te NULL'ları AYRI sayar — ihlalin tetiklenmesi için tenant altında koş
        // (TenantId dolu olsun); SQL Server'da NULL da tekil olduğundan davranış üretimle hizalı.
        var tenantId = SimpleGuidGenerator.Instance.Create();
        using (_currentTenant.Change(tenantId))
        {
            var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("RT1"));
            _companyContext.CompanyId = data.CompanyId;

            await WithUnitOfWorkAsync(() => _allocator.InsertNumberedAsync(NewVoucher(data, 42)));

            // Yarış simülasyonu: bayat numara (42 zaten alınmış) ile gelen ikinci fiş — ilk deneme
            // unique ihlali, retry MAX+1=43'ü YENİDEN hesaplar ve başarır; kullanıcıya hata sızmaz.
            var latecomer = NewVoucher(data, 42);
            await WithUnitOfWorkAsync(() => _allocator.InsertNumberedAsync(latecomer));

            latecomer.VoucherNumber.ShouldBe(43);

            var saved = await WithUnitOfWorkAsync(() => _voucherRepository.GetAsync(latecomer.Id));
            saved.VoucherNumber.ShouldBe(43);
        }
    }

    [Fact]
    public async Task Persistent_conflict_exhausts_retries_and_is_converted_to_NumberConflict()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        using (_currentTenant.Change(tenantId))
        {
            var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("NUM"));
            _companyContext.CompanyId = data.CompanyId;

            // KALICI tuzak: 1 numaralı fiş görünür; 2 numaralı fiş soft-delete edilir — MAX sorgusu
            // onu GÖRMEZ ama unique index satırı hâlâ TUTAR. Böylece her retry MAX+1=2'yi yeniden
            // üretir ve yine ihlale düşer → denemeler tükenir → lokalize NumberConflict.
            await WithUnitOfWorkAsync(() => _allocator.InsertNumberedAsync(NewVoucher(data, 1)));
            await WithUnitOfWorkAsync(async () =>
            {
                var decoy = NewVoucher(data, 2);
                await _voucherRepository.InsertAsync(decoy, autoSave: true);
                await _voucherRepository.DeleteAsync(decoy, autoSave: true);   // soft-delete: index satırı kalır
            });

            var ex = await Should.ThrowAsync<BusinessException>(
                () => WithUnitOfWorkAsync(() => _allocator.InsertNumberedAsync(NewVoucher(data, 2))));

            ex.Code.ShouldBe("TradeXpress:Voucher:NumberConflict");
        }
    }

    [Fact]
    public async Task Unrelated_db_update_exception_is_not_converted()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        using (_currentTenant.Change(tenantId))
        {
            var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("NC1"));
            _companyContext.CompanyId = data.CompanyId;

            // FK ihlali (var olmayan Branch): Sqlite'ta primary hata kodu yine 19 ama extended
            // kod UNIQUE değil (FOREIGN KEY) — NumberConflict'e ÇEVRİLMEMELİ, ham DbUpdateException
            // yüzeye çıkmalı (kök neden gizlenmez).
            var orphan = new Voucher(
                data.CompanyId,
                SimpleGuidGenerator.Instance.Create(),   // var olmayan şube → FK Restrict ihlali
                data.VaultId,
                AccountType.CurrentAccount,
                data.AccountId,
                "ACC",
                data.SubAccountId,
                "SUB",
                voucherNumber: 77,
                voucherDate: DateTime.Now);

            var ex = await Should.ThrowAsync<Exception>(
                () => WithUnitOfWorkAsync(() => _allocator.InsertNumberedAsync(orphan)));

            ex.ShouldNotBeAssignableTo<BusinessException>();
        }
    }

    [Fact]
    public void Detector_classifies_by_provider_error_code_not_message()
    {
        // Sqlite UNIQUE (19/2067) + hint mesajda → ihlal.
        var unique = new DbUpdateException(
            "insert failed",
            new SqliteException(
                "SQLite Error 19: 'UNIQUE constraint failed: AppVouchers.TenantId, " +
                "AppVouchers.CompanyId, AppVouchers.VoucherNumber'.", 19, 2067));
        _detector.IsUniqueConstraintViolation(unique, "VoucherNumber").ShouldBeTrue();

        // Hint mesajda YOKSA (başka bir unique index'in ihlali) → bu ihlale yorulmaz.
        _detector.IsUniqueConstraintViolation(unique, "SomeOtherColumn").ShouldBeFalse();

        // FK ihlali: primary kod aynı 19 ama extended 787 (FOREIGN KEY) → unique DEĞİL.
        var foreignKey = new DbUpdateException(
            "insert failed",
            new SqliteException("SQLite Error 19: 'FOREIGN KEY constraint failed'.", 19, 787));
        _detector.IsUniqueConstraintViolation(foreignKey).ShouldBeFalse();

        // Mesajı hint içeren ama kodu alakasız hata → ESKİ (mesaj-bazlı) tespit yanlış pozitifti,
        // kod-bazlı tespit reddeder.
        var unrelated = new DbUpdateException(
            "timeout while writing VoucherNumber",
            new TimeoutException("timeout while writing VoucherNumber"));
        _detector.IsUniqueConstraintViolation(unrelated, "VoucherNumber").ShouldBeFalse();
    }

    private static Voucher NewVoucher(VoucherTestData data, long number)
    {
        return new Voucher(
            data.CompanyId,
            data.BranchId,
            data.VaultId,
            AccountType.CurrentAccount,
            data.AccountId,
            "ACC",
            data.SubAccountId,
            "SUB",
            number,
            DateTime.Now,
            "allocator testi");
    }
}
