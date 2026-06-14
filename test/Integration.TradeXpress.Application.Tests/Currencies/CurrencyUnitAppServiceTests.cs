using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Currencies;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Currencies;

public abstract class CurrencyUnitAppServiceTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ICurrencyUnitAppService _appService;
    private readonly ICurrentTenant _currentTenant;

    protected CurrencyUnitAppServiceTests()
    {
        _appService = GetRequiredService<ICurrencyUnitAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Seed_should_expose_twelve_global_units()
    {
        var result = await _appService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 100 });

        result.TotalCount.ShouldBe(12);
        result.Items.ShouldAllBe(u => u.IsGlobal);
        result.Items.Select(u => u.Code).ShouldContain(CurrencyUnitCode.TRY);
    }

    [Fact]
    public async Task Create_then_update_identity_roundtrips()
    {
        var created = await _appService.CreateAsync(new CurrencyUnitCreateDto
        {
            Code = "XAU", Name = "Test Gold", Type = CurrencyUnitType.Metal,
        });

        created.IsGlobal.ShouldBeTrue(); // host context → TenantId null
        created.Code.ShouldBe("XAU");

        var updated = await _appService.UpdateAsync(created.Id, new CurrencyUnitUpdateDto
        {
            Name = "Test Gold 2", IsActive = false, DisplayOrder = 5,
        });

        updated.Name.ShouldBe("Test Gold 2");
        updated.IsActive.ShouldBeFalse();
        updated.DisplayOrder.ShouldBe(5);
    }

    [Fact]
    public async Task Deleting_a_system_unit_is_blocked()
    {
        var list = await _appService.GetListAsync(
            new CurrencyUnitListRequestDto { Filter = "USD", MaxResultCount = 10 });
        var usd = list.Items.Single(u => u.Code == CurrencyUnitCode.USD);

        await Should.ThrowAsync<BusinessException>(() => _appService.DeleteAsync(usd.Id));
    }

    [Fact]
    public async Task Following_a_unit_that_already_follows_is_blocked()
    {
        // child follows USD
        var usd = (await _appService.GetListAsync(new CurrencyUnitListRequestDto { Filter = "USD" }))
            .Items.Single(u => u.Code == CurrencyUnitCode.USD);

        var child = await _appService.CreateAsync(new CurrencyUnitCreateDto
        {
            Code = "CH1", Name = "Child", FollowingUnitId = usd.Id,
            FollowingMarginType = MarginType.Multiply, FollowingMarginValue = 1m,
        });

        // grandchild tries to follow child (which already follows) → single-level violation
        await Should.ThrowAsync<BusinessException>(() => _appService.CreateAsync(new CurrencyUnitCreateDto
        {
            Code = "CH2", Name = "Grandchild", FollowingUnitId = child.Id,
            FollowingMarginType = MarginType.Multiply, FollowingMarginValue = 1m,
        }));
    }

    [Fact]
    public async Task Tenant_sees_global_catalog_plus_its_own_units()
    {
        var tenantId = Guid.NewGuid();
        CurrencyUnitGetDto own;
        using (_currentTenant.Change(tenantId))
        {
            own = await _appService.CreateAsync(new CurrencyUnitCreateDto
            {
                Code = "TNT", Name = "Tenant Unit",
            });
            own.IsGlobal.ShouldBeFalse(); // tenant-owned

            var list = await _appService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 100 });
            list.TotalCount.ShouldBe(13); // 12 global + 1 own
            list.Items.Count(u => u.IsGlobal).ShouldBe(12);
            list.Items.ShouldContain(u => u.Code == "TNT" && !u.IsGlobal);
        }

        // Host must NOT see the tenant's unit.
        var hostList = await _appService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 100 });
        hostList.Items.ShouldNotContain(u => u.Code == "TNT");
    }
}
