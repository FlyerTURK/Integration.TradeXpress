using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

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
    public async Task Seed_should_expose_thirteen_global_units()
    {
        var result = await _appService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 100 });

        result.TotalCount.ShouldBe(13);   // 12 doviz/maden + AD (Adet sayim birimi, 2026-08-06)
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
            // Code aynı gönderilir: host biriminin kodu kilitli (HostCodeLocked) — değişmeyen kod no-op.
            Code = "XAU", Name = "Test Gold 2", IsActive = false, DisplayOrder = 5,
        });

        updated.Name.ShouldBe("Test Gold 2");
        updated.IsActive.ShouldBeFalse();
        updated.DisplayOrder.ShouldBe(5);
    }

    [Fact]
    public async Task Create_with_duplicate_code_gives_friendly_error()
    {
        // Standalone CurrencyUnit Create ön-benzersizlik kontrolü (Update ile aynı scope).
        await _appService.CreateAsync(new CurrencyUnitCreateDto
        {
            Code = "DUP", Name = "İlk", Type = CurrencyUnitType.Metal,
        });

        // Aynı kapsamda aynı kod tekrar → ham DB (TenantId, Code) unique çakışması DEĞİL, dostane hata.
        var ex = await Should.ThrowAsync<BusinessException>(() => _appService.CreateAsync(new CurrencyUnitCreateDto
        {
            Code = "DUP", Name = "İkinci", Type = CurrencyUnitType.Metal,
        }));
        ex.Code.ShouldBe("TradeXpress:CurrencyUnit:CodeAlreadyExists");
    }

    [Fact]
    public async Task Tenant_cannot_delete_a_global_unit()
    {
        var list = await _appService.GetListAsync(
            new CurrencyUnitListRequestDto { Filter = "USD", MaxResultCount = 10 });
        var usd = list.Items.Single(u => u.Code == CurrencyUnitCode.USD);

        // Yeni model: "system" = TenantId==null (global). Host global'i yönetebilir;
        // ama TENANT, global (host) birimi silemez/düzenleyemez.
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await Should.ThrowAsync<BusinessException>(() => _appService.DeleteAsync(usd.Id));
        }
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
            list.TotalCount.ShouldBe(14); // 13 global (12 doviz/maden + AD) + 1 own
            list.Items.Count(u => u.IsGlobal).ShouldBe(13);
            list.Items.ShouldContain(u => u.Code == "TNT" && !u.IsGlobal);
        }

        // Host must NOT see the tenant's unit.
        var hostList = await _appService.GetListAsync(new CurrencyUnitListRequestDto { MaxResultCount = 100 });
        hostList.Items.ShouldNotContain(u => u.Code == "TNT");
    }
}
