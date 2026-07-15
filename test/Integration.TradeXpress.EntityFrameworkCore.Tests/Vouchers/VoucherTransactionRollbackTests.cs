using System;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vouchers.Balance;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Çok-adımlı fiş yazımının ATOMİKLİK ağı: <c>SaveLineAsync</c> içinde birincil fiş kaydedildikten
/// SONRA (virman ikizinin insert'inde) GERÇEK bir DB hatası (VoucherNumber unique ihlali) patlarsa,
/// açık transaction (<c>[UnitOfWork(IsTransactional = true)]</c>) sayesinde birincil fişin, satırının
/// ve ledger kayıtlarının HİÇBİRİNİN kalıcı olmadığını doğrular. Global
/// <c>AddAlwaysDisableUnitOfWorkTransaction</c> yalnız otomatik UoW'ları kapatır — attribute'un açık
/// değeri onu bypass eder; bu test o garantinin regresyon ağıdır (mock yok, üretim yolu).
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class VoucherTransactionRollbackTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IVoucherAppService _voucherAppService;
    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledgerRepository;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;

    public VoucherTransactionRollbackTests()
    {
        _voucherAppService = GetRequiredService<IVoucherAppService>();
        _voucherRepository = GetRequiredService<IRepository<Voucher, Guid>>();
        _ledgerRepository  = GetRequiredService<IRepository<BalanceLedgerEntry, Guid>>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
        _currentTenant     = GetRequiredService<ICurrentTenant>();
        _dataFilter        = GetRequiredService<IDataFilter>();
    }

    [Fact]
    public async Task Failed_transfer_twin_insert_rolls_back_primary_voucher_and_ledger()
    {
        // Sqlite unique index NULL'ları AYRI sayar — ihlalin tetiklenmesi için tenant altında koş
        // (TenantId dolu; SQL Server'da davranış üretimle hizalı — bkz. VoucherNumberAllocatorTests).
        var tenantId = SimpleGuidGenerator.Instance.Create();
        using (_currentTenant.Change(tenantId))
        {
            var data      = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("RBK"));
            _companyContext.CompanyId = data.CompanyId;
            var counterId = await WithUnitOfWorkAsync(() => _seeder.SeedCounterSubAccountAsync(data));

            // TUZAK (gerçek numara-çakışması senaryosu): 2 numaralı fiş açılıp SOFT-DELETE edilir.
            // MAX(VoucherNumber) sorgusu soft-deleted satırı GÖRMEZ ama unique index (TenantId,
            // CompanyId, VoucherNumber) satırı hâlâ TUTAR. Böylece SaveLineAsync akışında:
            //   1. adım — birincil fiş NextNumber=1 ile BAŞARIYLA insert edilir (SaveChanges #1),
            //   2. adım — ledger senkronu (sil + yaz),
            //   3. adım — virman ikizinin fişi NextNumber=2 ister → unique ihlali → BusinessException.
            // Yani hata, İLK yazım kalıcı olduktan SONRA patlar — tam atomiklik senaryosu.
            await WithUnitOfWorkAsync(async () =>
            {
                var decoy = new Voucher(
                    data.CompanyId,
                    data.BranchId,
                    data.VaultId,
                    AccountType.CurrentAccount,
                    data.AccountId,
                    "ACC",
                    data.SubAccountId,
                    "SUB",
                    voucherNumber: 2,
                    voucherDate: DateTime.Now,
                    description: "numara tuzağı");
                await _voucherRepository.InsertAsync(decoy, autoSave: true);
                await _voucherRepository.DeleteAsync(decoy, autoSave: true);   // soft-delete: index satırı kalır
            });

            var ex = await Should.ThrowAsync<BusinessException>(() => _voucherAppService.SaveLineAsync(
                VoucherTestLines.TransferLine(data, counterId, ProcessDirectionType.Outbound, 500m)));
            ex.Code.ShouldBe("TradeXpress:Voucher:NumberConflict");

            // ATOMİKLİK: birincil fiş 1. adımda yazılmıştı — transaction'la GERİ ALINMIŞ olmalı.
            // Görünür fiş sayısı 0 (tuzak soft-deleted, filtreyle gizli); ledger tamamen boş.
            var visibleVouchers = await WithUnitOfWorkAsync(
                () => _voucherRepository.GetListAsync(v => v.CompanyId == data.CompanyId));
            visibleVouchers.ShouldBeEmpty();

            var ledgerEntries = await WithUnitOfWorkAsync(
                () => _ledgerRepository.GetListAsync(e => e.CompanyId == data.CompanyId));
            ledgerEntries.ShouldBeEmpty();

            // Sistem yarım durumda KALMAZ: tuzak temizlenince aynı kayıt sorunsuz geçer
            // (kullanıcı sözleşmesi — NumberConflict "tekrar deneyin" der).
            await WithUnitOfWorkAsync(async () =>
            {
                using (_dataFilter.Disable<ISoftDelete>())   // tuzak soft-deleted — filtre açıkken görünmez
                {
                    var decoy = await _voucherRepository.GetAsync(
                        v => v.CompanyId == data.CompanyId && v.VoucherNumber == 2, includeDetails: false);
                    await _voucherRepository.HardDeleteAsync(decoy);
                }
            });

            var saved = await _voucherAppService.SaveLineAsync(
                VoucherTestLines.TransferLine(data, counterId, ProcessDirectionType.Outbound, 500m));
            saved.VoucherId.ShouldNotBeNull();

            (await WithUnitOfWorkAsync(
                () => _ledgerRepository.GetListAsync(e => e.CompanyId == data.CompanyId)))
                .Count.ShouldBe(2);   // kaynak −500 + ikiz +500
        }
    }
}
