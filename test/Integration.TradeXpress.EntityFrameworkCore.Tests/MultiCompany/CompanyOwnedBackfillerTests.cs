using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Data;
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
    private readonly IDataFilter _dataFilter;

    public CompanyOwnedBackfillerTests()
    {
        _backfiller           = GetRequiredService<CompanyOwnedBackfiller>();
        _subAccountRepository = GetRequiredService<IRepository<SubAccount, Guid>>();
        _vaultRepository      = GetRequiredService<IRepository<Vault, Guid>>();
        _seeder               = GetRequiredService<VoucherTestDataSeeder>();
        _currentTenant        = GetRequiredService<ICurrentTenant>();
        _companyContext       = GetRequiredService<TestCompanyContextProvider>();
        _dbContextProvider    = GetRequiredService<IDbContextProvider<TradeXpressDbContext>>();
        _dataFilter           = GetRequiredService<IDataFilter>();
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
            await WithUnitOfWorkAsync(() => _backfiller.BackfillAllTenantsAsync());

            var sub = await WithUnitOfWorkAsync(() => _subAccountRepository.GetAsync(data.SubAccountId));
            var vault = await WithUnitOfWorkAsync(() => _vaultRepository.GetAsync(data.VaultId));
            sub.CompanyId.ShouldBe(data.CompanyId);
            vault.CompanyId.ShouldBe(data.CompanyId);

            // ikinci koşu: idempotent → hata yok, değer bozulmaz (artık Guid.Empty satır yok).
            await WithUnitOfWorkAsync(() => _backfiller.BackfillAllTenantsAsync());

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
            await WithUnitOfWorkAsync(() => _backfiller.BackfillAllTenantsAsync());

            var sub = await WithUnitOfWorkAsync(() => _subAccountRepository.GetAsync(data.SubAccountId));
            var vault = await WithUnitOfWorkAsync(() => _vaultRepository.GetAsync(data.VaultId));
            sub.CompanyId.ShouldBe(data.CompanyId);
            vault.CompanyId.ShouldBe(data.CompanyId);
        }
    }

    [Fact]
    public async Task Backfills_all_tenants_in_a_single_run()
    {
        // Regresyon: seeder tenant-scoped çalışırsa yalnız aktif tenant'ın boş kayıtları dolar (canlıda
        // 10 tenant'ın 9'u boş kalmıştı). BackfillAllTenantsAsync Disable<IMultiTenant> ile TÜM tenant'ları
        // TEK koşuda kapsamalı — hangi tenant context'inde tetiklenirse tetiklensin.
        var (tenantA, dataA) = await SeedAsync();
        var (tenantB, dataB) = await SeedAsync();

        _companyContext.CompanyId = null; // konsolide → Guid.Empty görünür

        // İki farklı tenant'ın kayıtlarını da boşalt (migration-sonrası durum).
        await ForceEmptyCompanyAsync(dataA);
        await ForceEmptyCompanyAsync(dataB);

        // TEK koşu, tenantA context'inde — ama tenantB'nin kayıtları da dolmalı.
        using (_currentTenant.Change(tenantA))
        {
            await WithUnitOfWorkAsync(() => _backfiller.BackfillAllTenantsAsync());
        }

        // Doğrulama: her iki tenant'ın da kayıtları parent'tan dolduruldu (tenant filtresi kapalı okuma).
        var subA = await ReadSubAccountAcrossTenantsAsync(dataA.SubAccountId);
        var vaultA = await ReadVaultAcrossTenantsAsync(dataA.VaultId);
        var subB = await ReadSubAccountAcrossTenantsAsync(dataB.SubAccountId);
        var vaultB = await ReadVaultAcrossTenantsAsync(dataB.VaultId);

        subA.CompanyId.ShouldBe(dataA.CompanyId);
        vaultA.CompanyId.ShouldBe(dataA.CompanyId);
        subB.CompanyId.ShouldBe(dataB.CompanyId);
        vaultB.CompanyId.ShouldBe(dataB.CompanyId);
    }

    // ── kurulum / yardımcılar ────────────────────────────────────────────────

    private Task<SubAccount> ReadSubAccountAcrossTenantsAsync(Guid id)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<IMultiTenant>())
            {
                return await _subAccountRepository.GetAsync(id);
            }
        });
    }

    private Task<Vault> ReadVaultAcrossTenantsAsync(Guid id)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<IMultiTenant>())
            {
                return await _vaultRepository.GetAsync(id);
            }
        });
    }

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
