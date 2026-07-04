using System;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Vault çok-şirket GÜVENLİK SINIRI regresyon ağı — <see cref="Vault"/> artık <see cref="ICompanyOwned"/>
/// (CompanyId parent şubeden DENORMALİZE). Daha önce Vault'ta company kolonu YOKTU → yabancı şirketin
/// kasası working-context'te görünüyordu (cross-company okuma/güncelleme/silme). Bu ağ: yabancı kasa
/// working-context'te YOKMUŞ gibi davranır (Get/Update/Delete → EntityNotFound), delete başarısız olunca
/// yabancı satır YIKILMAZ ve CompanyId parent şubeden türetilir.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class VaultCompanyScopeTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IVaultAppService _vaultAppService;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;

    public VaultCompanyScopeTests()
    {
        _vaultAppService = GetRequiredService<IVaultAppService>();
        _vaultRepository = GetRequiredService<IRepository<Vault, Guid>>();
        _seeder          = GetRequiredService<VoucherTestDataSeeder>();
        _currentTenant   = GetRequiredService<ICurrentTenant>();
        _companyContext  = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task Foreign_company_vault_get_behaves_as_not_found()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            await Should.ThrowAsync<EntityNotFoundException>(
                () => _vaultAppService.GetAsync(data.Foreign.VaultId));

            // Kontrol grubu: kendi şirketinin kasası normal açılır.
            (await _vaultAppService.GetAsync(data.Mine.VaultId)).Id.ShouldBe(data.Mine.VaultId);
        }
    }

    [Fact]
    public async Task Foreign_company_vault_update_behaves_as_not_found()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            await Should.ThrowAsync<EntityNotFoundException>(
                () => _vaultAppService.UpdateAsync(data.Foreign.VaultId, BuildUpdate()));
        }
    }

    [Fact]
    public async Task Foreign_company_vault_delete_behaves_as_not_found_and_leaves_it_intact()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            // IDOR ağı: yabancı kasa silinemez — GetAsync company-filter altında NotFound eder
            // (son-kasa kuralı kontrolünden ÖNCE, yabancı satıra hiç dokunmadan).
            await Should.ThrowAsync<EntityNotFoundException>(
                () => _vaultAppService.DeleteAsync(data.Foreign.VaultId));

            // Doğrulamada yabancı şirket context'ine geç (aksi hâlde satır zaten filtreyle GİZLİ).
            _companyContext.CompanyId = data.Foreign.CompanyId;
            var stillExists = await WithUnitOfWorkAsync(
                () => _vaultRepository.AnyAsync(v => v.Id == data.Foreign.VaultId));
            stillExists.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Create_derives_company_from_parent_branch()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            // CompanyId client'tan gelmez; parent şubeden (mine) denormalize edilir.
            var created = await _vaultAppService.CreateAsync(new VaultCreateDto
            {
                BranchId = data.Mine.BranchId,
                Code     = "NEWVLT",
                Name     = "New Vault",
            });

            var entity = await WithUnitOfWorkAsync(() => _vaultRepository.GetAsync(created.Id));
            entity.CompanyId.ShouldBe(data.Mine.CompanyId);
        }
    }

    [Fact]
    public async Task Consolidated_context_sees_all_companies()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = null; // konsolide → kısıt yok

            var visible = await WithUnitOfWorkAsync(() => _vaultRepository.GetListAsync(
                v => v.Id == data.Mine.VaultId || v.Id == data.Foreign.VaultId));

            visible.Count.ShouldBe(2);
        }
    }

    // ── kurulum / yardımcılar ────────────────────────────────────────────────

    private async Task<VaultScopeData> SeedAsync()
    {
        _companyContext.CompanyId = null; // seed sırasında context yok

        var tenantId = SimpleGuidGenerator.Instance.Create();

        VoucherTestData mine, foreign;
        using (_currentTenant.Change(tenantId))
        {
            (mine, foreign) = await WithUnitOfWorkAsync(async () =>
            {
                var m = await _seeder.SeedCompanyGraphAsync("VS1");
                var f = await _seeder.SeedCompanyGraphAsync("VS2");
                return (m, f);
            });
        }

        return new VaultScopeData(tenantId, mine, foreign);
    }

    private static VaultUpdateDto BuildUpdate()
    {
        return new VaultUpdateDto
        {
            Code     = "RENVLT",
            Name     = "Renamed Vault",
            IsActive = true,
        };
    }

    private sealed record VaultScopeData(
        Guid TenantId,
        VoucherTestData Mine,
        VoucherTestData Foreign);
}
