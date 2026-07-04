using System;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.AssayOffices;

/// <summary>
/// AssayOffice (ayar evi) — company-scoped Create ön-benzersizlik kontrolü regresyon testi: aynı şirkette
/// aynı kod tekrar → ham DB (TenantId, CompanyId, Code) unique çakışması DEĞİL, dostane BusinessException.
/// Working şirket <see cref="TestCompanyContextProvider"/> ile belirlenir (Account desenindeki gibi).
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class AssayOfficeAppServiceTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IAssayOfficeAppService _appService;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;

    public AssayOfficeAppServiceTests()
    {
        _appService = GetRequiredService<IAssayOfficeAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task Create_with_duplicate_code_in_same_company_gives_friendly_error()
    {
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = companyId;

            await WithUnitOfWorkAsync(
                () => _appService.CreateAsync(new AssayOfficeCreateDto { Code = "AYR", Name = "Ayar Evi" }));

            (await Should.ThrowAsync<BusinessException>(
                    () => WithUnitOfWorkAsync(
                        () => _appService.CreateAsync(new AssayOfficeCreateDto { Code = "AYR", Name = "İkinci" }))))
                .Code.ShouldBe("TradeXpress:AssayOffice:CodeAlreadyExists");
        }
    }
}
