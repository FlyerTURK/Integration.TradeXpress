using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Company/Branch/Vault kod-benzersizliği regresyon ağı. Üçünde de aynı kodlu ikinci kayıt → ham DB unique
/// çakışması DEĞİL, dostane <c>{Entity}:CodeAlreadyExists</c> BusinessException (Account/SubAccount ile simetrik
/// Create ön-kontrolü). Scope: Company=tenant, Branch=company, Vault=branch.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class OrgCodeUniquenessTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ICompanyAppService _companyAppService;
    private readonly IBranchAppService _branchAppService;
    private readonly IVaultAppService _vaultAppService;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly IRepository<Country, Guid> _countryRepository;
    private readonly ICurrentTenant _currentTenant;

    public OrgCodeUniquenessTests()
    {
        _companyAppService = GetRequiredService<ICompanyAppService>();
        _branchAppService  = GetRequiredService<IBranchAppService>();
        _vaultAppService   = GetRequiredService<IVaultAppService>();
        _seeder            = GetRequiredService<VoucherTestDataSeeder>();
        _countryRepository = GetRequiredService<IRepository<Country, Guid>>();
        _currentTenant     = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Duplicate_company_code_in_same_tenant_gives_friendly_error()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var data = await WithUnitOfWorkAsync(() =>
        {
            using (_currentTenant.Change(tenantId))
                return _seeder.SeedCompanyGraphAsync("OCU_CO");
        });
        var countryId = await WithUnitOfWorkAsync(async () =>
            (await _countryRepository.GetListAsync()).First().Id);

        using (_currentTenant.Change(tenantId))
        {
            var create = new CompanyCreateDto
            {
                Code = "DUPCO",
                Name = "Dup Company",
                CountryId = countryId,
                BaseCurrencyUnitId = data.TryUnitId,
            };

            await WithUnitOfWorkAsync(() => _companyAppService.CreateAsync(create));

            (await Should.ThrowAsync<BusinessException>(
                    () => WithUnitOfWorkAsync(() => _companyAppService.CreateAsync(create))))
                .Code.ShouldBe("TradeXpress:Company:CodeAlreadyExists");
        }
    }

    [Fact]
    public async Task Duplicate_branch_code_in_same_company_gives_friendly_error()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var data = await WithUnitOfWorkAsync(() =>
        {
            using (_currentTenant.Change(tenantId))
                return _seeder.SeedCompanyGraphAsync("OCU_BR");
        });

        using (_currentTenant.Change(tenantId))
        {
            var create = new BranchCreateDto { CompanyId = data.CompanyId, Code = "DUPBR", Name = "Dup Branch" };

            await WithUnitOfWorkAsync(() => _branchAppService.CreateAsync(create));

            (await Should.ThrowAsync<BusinessException>(
                    () => WithUnitOfWorkAsync(() => _branchAppService.CreateAsync(create))))
                .Code.ShouldBe("TradeXpress:Branch:CodeAlreadyExists");
        }
    }

    [Fact]
    public async Task Duplicate_vault_code_in_same_branch_gives_friendly_error()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var data = await WithUnitOfWorkAsync(() =>
        {
            using (_currentTenant.Change(tenantId))
                return _seeder.SeedCompanyGraphAsync("OCU_VL");
        });

        using (_currentTenant.Change(tenantId))
        {
            var create = new VaultCreateDto { BranchId = data.BranchId, Code = "DUPVLT", Name = "Dup Vault" };

            await WithUnitOfWorkAsync(() => _vaultAppService.CreateAsync(create));

            (await Should.ThrowAsync<BusinessException>(
                    () => WithUnitOfWorkAsync(() => _vaultAppService.CreateAsync(create))))
                .Code.ShouldBe("TradeXpress:Vault:CodeAlreadyExists");
        }
    }
}
