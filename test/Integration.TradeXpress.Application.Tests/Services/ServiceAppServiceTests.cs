using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Services;

/// <summary>
/// ServiceAppService pilotu üzerinden <c>HostCatalogCrudAppService</c> TABAN davranış testleri:
/// host‖own görünürlük, tenant'ın global kaydı düzenleyememesi/silememesi, IsGlobal enrichment,
/// picker scope'u ve whitelist reddi. Taban bir kez burada test edilir; diğer katalog servisleri
/// (Cash/Metal/Stone/...) aynı tabanı paylaştığı için ayrı ayrı test edilmez.
/// </summary>
public abstract class ServiceAppServiceTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IServiceAppService _appService;
    private readonly ICurrentTenant _currentTenant;

    protected ServiceAppServiceTests()
    {
        _appService = GetRequiredService<IServiceAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Host_creates_global_record_and_can_update_and_delete_it()
    {
        var created = await _appService.CreateAsync(new ServiceCreateDto
        {
            Code = "RAF", Name = "Rafinaj",
        });

        created.IsGlobal.ShouldBeTrue(); // host context → TenantId null
        created.Code.ShouldBe("RAF");

        var updated = await _appService.UpdateAsync(created.Id, new ServiceUpdateDto
        {
            Name = "Rafinaj Ücreti", IsActive = false,
        });

        updated.Name.ShouldBe("Rafinaj Ücreti");
        updated.IsActive.ShouldBeFalse();

        await _appService.DeleteAsync(created.Id);
        await Should.ThrowAsync<Exception>(() => _appService.GetAsync(created.Id));
    }

    [Fact]
    public async Task Tenant_sees_global_catalog_plus_its_own_records()
    {
        var global = await _appService.CreateAsync(new ServiceCreateDto
        {
            Code = "KMS", Name = "Komisyon",
        });

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var own = await _appService.CreateAsync(new ServiceCreateDto
            {
                Code = "OZL", Name = "Özel Hizmet",
            });
            own.IsGlobal.ShouldBeFalse(); // tenant-owned

            var list = await _appService.GetListAsync(new ServiceListRequestDto { MaxResultCount = 100 });
            list.Items.ShouldContain(s => s.Code == "KMS" && s.IsGlobal);
            list.Items.ShouldContain(s => s.Code == "OZL" && !s.IsGlobal);
        }

        // Host, tenant'ın kaydını GÖRMEZ.
        var hostList = await _appService.GetListAsync(new ServiceListRequestDto { MaxResultCount = 100 });
        hostList.Items.ShouldNotContain(s => s.Code == "OZL");
        hostList.Items.ShouldContain(s => s.Code == "KMS");
    }

    [Fact]
    public async Task Tenant_cannot_update_or_delete_a_global_record()
    {
        var global = await _appService.CreateAsync(new ServiceCreateDto
        {
            Code = "SDK", Name = "Sadekar İşçiliği",
        });

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            // Görür ama düzenleyemez/silemez — error-code AYNEN korunmuş olmalı.
            var visible = await _appService.GetAsync(global.Id);
            visible.IsGlobal.ShouldBeTrue();

            var editEx = await Should.ThrowAsync<BusinessException>(
                () => _appService.UpdateAsync(global.Id, new ServiceUpdateDto { Name = "Hack", IsActive = true }));
            editEx.Code.ShouldBe("TradeXpress:Service:CannotEditGlobalAsTenant");

            var deleteEx = await Should.ThrowAsync<BusinessException>(
                () => _appService.DeleteAsync(global.Id));
            deleteEx.Code.ShouldBe("TradeXpress:Service:CannotDeleteGlobalAsTenant");
        }
    }

    [Fact]
    public async Task Tenant_can_update_its_own_record()
    {
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var own = await _appService.CreateAsync(new ServiceCreateDto
            {
                Code = "TMR", Name = "Tamir",
            });

            var updated = await _appService.UpdateAsync(own.Id, new ServiceUpdateDto
            {
                Name = "Tamir Bakım", IsActive = true,
            });

            updated.Name.ShouldBe("Tamir Bakım");
            updated.IsGlobal.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Picker_returns_host_and_own_records_ordered_by_code_including_passives()
    {
        var passive = await _appService.CreateAsync(new ServiceCreateDto { Code = "AAA", Name = "Pasif Hizmet" });
        await _appService.UpdateAsync(passive.Id, new ServiceUpdateDto { Name = "Pasif Hizmet", IsActive = false });
        await _appService.CreateAsync(new ServiceCreateDto { Code = "ZZZ", Name = "Son Hizmet" });

        using (_currentTenant.Change(Guid.NewGuid()))
        {
            await _appService.CreateAsync(new ServiceCreateDto { Code = "MMM", Name = "Tenant Hizmeti" });

            var picker = await _appService.GetPickerListAsync();

            picker.ShouldContain(s => s.Code == "AAA" && !s.IsActive); // pasif dahil
            picker.ShouldContain(s => s.Code == "MMM");
            picker.ShouldContain(s => s.Code == "ZZZ");

            var codes = picker.Select(s => s.Code).ToList();
            codes.ShouldBe(codes.OrderBy(c => c, StringComparer.Ordinal).ToList()); // koda göre sıralı
        }

        // Host picker'ı tenant kaydını içermez.
        var hostPicker = await _appService.GetPickerListAsync();
        hostPicker.ShouldNotContain(s => s.Code == "MMM");
    }

    [Fact]
    public async Task Sorting_by_a_field_outside_the_whitelist_is_rejected()
    {
        await Should.ThrowAsync<ListQueryException>(() => _appService.GetListAsync(new ServiceListRequestDto
        {
            Sorting = "Description",
            MaxResultCount = 10,
        }));
    }
}
