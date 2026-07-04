using System;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Sqlite in-memory (paylaşımlı bağlantı) test ortamında AÇIK transactional UoW rollback makinesinin
/// çalıştığını voucher akışından BAĞIMSIZ doğrular — <see cref="VoucherTransactionRollbackTests"/>'in
/// izole tabanı. Global default Disabled kaldığından transaction YALNIZ açık opt-in ile kurulur;
/// bu test o opt-in'in gerçekten transaction açtığının (Complete'siz dispose → geri alma) kanıtıdır.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class UnitOfWorkTransactionTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _uowManager;

    public UnitOfWorkTransactionTests()
    {
        _voucherRepository = GetRequiredService<IRepository<Voucher, Guid>>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
        _currentTenant     = GetRequiredService<ICurrentTenant>();
        _uowManager        = GetRequiredService<IUnitOfWorkManager>();
    }

    [Fact]
    public async Task Explicit_transactional_uow_rolls_back_autosaved_insert()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        using (_currentTenant.Change(tenantId))
        {
            var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("PRB"));
            _companyContext.CompanyId = data.CompanyId;

            using (var uow = _uowManager.Begin(
                new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
            {
                await _voucherRepository.InsertAsync(
                    new Voucher(
                        data.CompanyId, data.BranchId, data.VaultId,
                        data.AccountId, data.SubAccountId, 99, DateTime.Now, "probe"),
                    autoSave: true);
                // Complete YOK — dispose'da rollback beklenir.
            }

            var vouchers = await WithUnitOfWorkAsync(
                () => _voucherRepository.GetListAsync(v => v.CompanyId == data.CompanyId));
            vouchers.ShouldBeEmpty();
        }
    }
}
