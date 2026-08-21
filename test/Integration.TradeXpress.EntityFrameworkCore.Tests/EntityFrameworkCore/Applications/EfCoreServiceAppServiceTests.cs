using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Services;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

/// <summary>
/// ServiceAppService pilotu üzerinden <c>HostCatalogCrudAppService</c> TABAN davranış testleri
/// (host kaydına dokunma guard'ı, picker sırası, dostane benzersizlik hatası, sıralama whitelist'i) —
/// taban bir kez burada test edilir; aynı tabanı paylaşan diğer katalog servisleri ayrı ayrı test edilmez.
///
/// <para><b>Görev #4 (per-company) sonrası:</b> Service artık <see cref="ICompanyOwned"/> bir EMTİA'dır.
/// Bu yüzden testler Application.Tests'ten BURAYA taşındı — sahiplik zorlaması working-company bağlamı
/// ister (<see cref="TestCompanyContextProvider"/> yalnız bu projede). Eski dosyanın "host global kayıt
/// ÜRETİR" iddiaları bilinçli olarak DÜŞTÜ: şirketsiz bağlamda üretim artık fail-closed reddedilir
/// (<c>CompanyOwnershipGuard</c>). Yerine sahiplik sınırının kendisi test edilir; taban davranışın
/// kapsamı DARALMADI, kaydırıldı. KIRMIZIYSA sınır delinmiş demektir — testi gevşetme.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreServiceAppServiceTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IServiceAppService _appService;
    private readonly IRepository<Service, Guid> _services;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;

    public EfCoreServiceAppServiceTests()
    {
        _appService     = GetRequiredService<IServiceAppService>();
        _services       = GetRequiredService<IRepository<Service, Guid>>();
        _currentTenant  = GetRequiredService<ICurrentTenant>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    /// <summary>Working şirket yokken emtia üretimi FAIL-CLOSED reddedilir — sahipsiz ("holding") kayıt,
    /// tenant'ın tüm şirketlerine görünür olduğundan cross-company manipülasyonun taşıyıcısıydı.</summary>
    [Fact]
    public async Task Create_without_a_working_company_is_rejected()
    {
        _companyContext.CompanyId = null;

        using (_currentTenant.Change(SimpleGuidGenerator.Instance.Create()))
        {
            var ex = await Should.ThrowAsync<BusinessException>(
                () => _appService.CreateAsync(NewService("NOCO")));

            ex.Code.ShouldBe("TradeXpress:MultiCompany:WorkingCompanyRequired");
        }
    }

    /// <summary>Sahiplik client'tan DEĞİL working şirketten damgalanır (input'ta CompanyId alanı yoktur).</summary>
    [Fact]
    public async Task Create_stamps_the_working_company_as_the_owner()
    {
        var tenantId  = SimpleGuidGenerator.Instance.Create();
        var companyId = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = companyId;

            var created = await _appService.CreateAsync(NewService("OWN"));
            created.IsGlobal.ShouldBeFalse();

            var entity = await WithUnitOfWorkAsync(() => _services.GetAsync(created.Id));
            entity.CompanyId.ShouldBe(companyId);
            entity.TenantId.ShouldBe(tenantId);
        }
    }

    /// <summary>Kardeş şirketin kaydı GÖRÜNMEZ — güvenlik sınırının asıl iddiası.</summary>
    [Fact]
    public async Task Records_of_a_sibling_company_are_not_visible()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var companyA = SimpleGuidGenerator.Instance.Create();
        var companyB = SimpleGuidGenerator.Instance.Create();
        var code     = UniqueCode("SIB");

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = companyA;
            await _appService.CreateAsync(NewService("SIB", code));

            _companyContext.CompanyId = companyB;
            var list = await _appService.GetListAsync(new ServiceListRequestDto { MaxResultCount = 100 });

            list.Items.ShouldNotContain(s => s.Code == code);
        }
    }

    /// <summary>Benzersizlik artık ŞİRKET kapsamındadır: aynı kod kardeş şirkette SERBEST, kendi şirketinde
    /// dostane hata (ham DB unique çakışması değil).</summary>
    [Fact]
    public async Task Duplicate_code_is_rejected_within_the_company_but_allowed_in_a_sibling()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var companyA = SimpleGuidGenerator.Instance.Create();
        var companyB = SimpleGuidGenerator.Instance.Create();
        var code     = UniqueCode("DUP");

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = companyA;
            await _appService.CreateAsync(NewService("DUP", code));

            var ex = await Should.ThrowAsync<BusinessException>(
                () => _appService.CreateAsync(NewService("DUP", code)));
            ex.Code.ShouldBe("TradeXpress:Service:CodeAlreadyExists");

            // Kardeş şirket AYNI kodu kullanabilir — katalog artık şirkete aittir.
            _companyContext.CompanyId = companyB;
            var sibling = await _appService.CreateAsync(NewService("DUP", code));
            sibling.Code.ShouldBe(code);
        }
    }

    /// <summary>Tenant, host kataloğu kaydını (TenantId=null) düzenleyemez/silemez — taban guard'ı
    /// (error-code'lar AYNEN korunur). Host kaydı repository ile seed'lenir: per-company modelde
    /// AppService üzerinden host kaydı üretilemez (şirket bağlamı yok).</summary>
    [Fact]
    public async Task Tenant_cannot_update_or_delete_a_host_record()
    {
        var code = UniqueCode("HST");

        var hostId = await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(null))
            {
                var entity = await _services.InsertAsync(
                    new Service(code, "Host Katalog Hizmeti", SimpleGuidGenerator.Instance.Create()),
                    autoSave: true);
                return entity.Id;
            }
        });

        using (_currentTenant.Change(SimpleGuidGenerator.Instance.Create()))
        {
            _companyContext.CompanyId = SimpleGuidGenerator.Instance.Create();

            var visible = await _appService.GetAsync(hostId);
            visible.IsGlobal.ShouldBeTrue();

            var editEx = await Should.ThrowAsync<BusinessException>(
                () => _appService.UpdateAsync(hostId, new ServiceUpdateDto { Code = code, Name = "Hack", IsActive = true }));
            editEx.Code.ShouldBe("TradeXpress:Service:CannotEditGlobalAsTenant");

            var deleteEx = await Should.ThrowAsync<BusinessException>(() => _appService.DeleteAsync(hostId));
            deleteEx.Code.ShouldBe("TradeXpress:Service:CannotDeleteGlobalAsTenant");
        }
    }

    [Fact]
    public async Task Tenant_can_update_and_delete_its_own_record()
    {
        using (_currentTenant.Change(SimpleGuidGenerator.Instance.Create()))
        {
            _companyContext.CompanyId = SimpleGuidGenerator.Instance.Create();

            var code = UniqueCode("UPD");
            var own  = await _appService.CreateAsync(NewService("UPD", code));

            var updated = await _appService.UpdateAsync(own.Id, new ServiceUpdateDto
            {
                Code = code, Name = "Tamir Bakım", IsActive = true,
            });

            updated.Name.ShouldBe("Tamir Bakım");
            updated.IsGlobal.ShouldBeFalse();

            await _appService.DeleteAsync(own.Id);
            await Should.ThrowAsync<Exception>(() => _appService.GetAsync(own.Id));
        }
    }

    /// <summary>Taban picker'ı (<c>GetPickerListCoreAsync</c>): kendi şirketinin kayıtları, PASİFLER DAHİL,
    /// koda göre sıralı.</summary>
    [Fact]
    public async Task Picker_returns_own_company_records_ordered_by_code_including_passives()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var companyA = SimpleGuidGenerator.Instance.Create();
        var companyB = SimpleGuidGenerator.Instance.Create();
        var passiveCode = UniqueCode("AAA");
        var activeCode  = UniqueCode("ZZZ");
        var siblingCode = UniqueCode("MMM");

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = companyB;
            await _appService.CreateAsync(NewService("MMM", siblingCode));

            _companyContext.CompanyId = companyA;
            var passive = await _appService.CreateAsync(NewService("AAA", passiveCode));
            await _appService.UpdateAsync(passive.Id, new ServiceUpdateDto
            {
                Code = passiveCode, Name = "Pasif Hizmet", IsActive = false,
            });
            await _appService.CreateAsync(NewService("ZZZ", activeCode));

            var picker = await _appService.GetPickerListAsync();

            picker.ShouldContain(s => s.Code == passiveCode && !s.IsActive);   // pasif dahil
            picker.ShouldContain(s => s.Code == activeCode);
            picker.ShouldNotContain(s => s.Code == siblingCode);               // kardeş şirket YOK

            var codes = picker.Select(s => s.Code).ToList();
            codes.ShouldBe(codes.OrderBy(c => c, StringComparer.Ordinal).ToList());
        }
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

    /// <summary>Paylaşılan Sqlite collection DB'sinde çakışmasın diye test-başına benzersiz kod.</summary>
    private static string UniqueCode(string prefix)
    {
        return $"{prefix}{SimpleGuidGenerator.Instance.Create().ToString("N")[..6].ToUpperInvariant()}";
    }

    private static ServiceCreateDto NewService(string prefix, string? code = null)
    {
        return new ServiceCreateDto
        {
            Code = code ?? UniqueCode(prefix),
            Name = $"{prefix} Hizmeti",
        };
    }
}
