using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// <see cref="CompanyOwnedBackfiller"/> geçiş-backfill ağı: <see cref="SubAccount"/>/<see cref="Vault"/>
/// <c>ICompanyOwned</c>'a taşınırken EF migration'ı <c>CompanyId</c>'yi <c>Guid.Empty</c> bıraktığında,
/// seeder mevcut satırları parent'tan (Account/Branch) doldurmalı ve ikinci koşuda no-op olmalı.
/// Sqlite test DB'si model-tabanlı (CreateTables) olduğundan gerçek satırlar zaten CompanyId ile insert
/// edilir; migration-sonrası boş durumu ham SQL ile SİMÜLE ederiz.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class CompanyOwnedBackfillerTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly CompanyOwnedBackfiller _backfiller;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly IDbContextProvider<TradeXpressDbContext> _dbContextProvider;

    public CompanyOwnedBackfillerTests()
    {
        _backfiller           = GetRequiredService<CompanyOwnedBackfiller>();
        _subAccountRepository = GetRequiredService<IRepository<SubAccount, Guid>>();
        _vaultRepository      = GetRequiredService<IRepository<Vault, Guid>>();
        _seeder               = GetRequiredService<VoucherTestDataSeeder>();
        _currentTenant        = GetRequiredService<ICurrentTenant>();
        _companyContext       = GetRequiredService<TestCompanyContextProvider>();
        _dbContextProvider    = GetRequiredService<IDbContextProvider<TradeXpressDbContext>>();
    }

    [Fact]
    public async Task Backfills_empty_company_from_parent_and_is_idempotent()
    {
        var (tenantId, data) = await SeedAsync();

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = null; // konsolide → company filtresi permissive (Guid.Empty görünür)

            // migration-sonrası durumu SİMÜLE et: satırların CompanyId'sini boşalt (ham SQL, change-tracker bypass).
            await ForceEmptyCompanyAsync(data);

            // ilk koşu: parent'tan doldurur.
            await WithUnitOfWorkAsync(() => _backfiller.BackfillCurrentTenantAsync());

            var sub = await WithUnitOfWorkAsync(() => _subAccountRepository.GetAsync(data.SubAccountId));
            var vault = await WithUnitOfWorkAsync(() => _vaultRepository.GetAsync(data.VaultId));
            sub.CompanyId.ShouldBe(data.CompanyId);
            vault.CompanyId.ShouldBe(data.CompanyId);

            // ikinci koşu: idempotent → hata yok, değer bozulmaz (artık Guid.Empty satır yok).
            await WithUnitOfWorkAsync(() => _backfiller.BackfillCurrentTenantAsync());

            var subAgain = await WithUnitOfWorkAsync(() => _subAccountRepository.GetAsync(data.SubAccountId));
            var vaultAgain = await WithUnitOfWorkAsync(() => _vaultRepository.GetAsync(data.VaultId));
            subAgain.CompanyId.ShouldBe(data.CompanyId);
            vaultAgain.CompanyId.ShouldBe(data.CompanyId);
        }
    }

    [Fact]
    public async Task Clean_install_without_empty_rows_is_noop()
    {
        var (tenantId, data) = await SeedAsync();

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = null;

            // Hiç Guid.Empty satır yok (temiz kurulum) → no-op; mevcut doğru CompanyId'ler korunur.
            await WithUnitOfWorkAsync(() => _backfiller.BackfillCurrentTenantAsync());

            var sub = await WithUnitOfWorkAsync(() => _subAccountRepository.GetAsync(data.SubAccountId));
            var vault = await WithUnitOfWorkAsync(() => _vaultRepository.GetAsync(data.VaultId));
            sub.CompanyId.ShouldBe(data.CompanyId);
            vault.CompanyId.ShouldBe(data.CompanyId);
        }
    }

    // ── kurulum / yardımcılar ────────────────────────────────────────────────

    private async Task<(Guid TenantId, VoucherTestData Data)> SeedAsync()
    {
        _companyContext.CompanyId = null;

        var tenantId = SimpleGuidGenerator.Instance.Create();

        VoucherTestData data;
        using (_currentTenant.Change(tenantId))
        {
            data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync("BF1"));
        }

        return (tenantId, data);
    }

    /// <summary>Seedlenen SubAccount/Vault'un CompanyId'sini ham SQL ile Guid.Empty yapar (migration'ın
    /// bıraktığı defaultValue durumunu simüle eder; entity ctor'u Guid.Empty'ye izin vermez → SQL şart).</summary>
    private async Task ForceEmptyCompanyAsync(VoucherTestData data)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await _dbContextProvider.GetDbContextAsync();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE AppSubAccounts SET CompanyId = {0} WHERE Id = {1}", Guid.Empty, data.SubAccountId);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE AppVaults SET CompanyId = {0} WHERE Id = {1}", Guid.Empty, data.VaultId);
        });
    }
}
