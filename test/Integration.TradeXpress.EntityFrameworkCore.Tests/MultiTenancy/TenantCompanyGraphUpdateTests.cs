using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Tenants;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Xunit;

namespace Integration.TradeXpress.MultiTenancy;

/// <summary>
/// TENANT GÜNCELLEMESİ ŞİRKET GRAFINI GERÇEKTEN YAZAR — uçtan uca.
///
/// <para><b>Kapatılan açık:</b> tenant güncelleme formundaki şirket→şube→kasa drill'i tam yetkiliydi
/// (ekle / sil / Merkez Yap) ve kullanıcı düzenlemesini kaydediyordu. Ama <c>TenantUpdateDto</c> yalnız
/// <c>Name</c> taşıyordu ve <c>UpdateAsync</c> yalnız <c>ChangeNameAsync</c> çağırıyordu → yapılan HER
/// yapısal değişiklik sessizce çöpe gidiyordu. Ne hata, ne uyarı; yalnız "kaydedildi" diyen bir form.</para>
///
/// <para><b>Neden entegrasyon testi:</b> sıra kuralları <c>TenantCompanyGraphPlannerTests</c>'te altyapısız
/// sürülüyor. Burada sürülen şey BAĞLANTI: UpdateAsync gerçekten planı çalıştırıyor mu, impersonation
/// yetkiyi taşıyor mu, yazım DB'ye ULAŞIYOR mu. Hatanın kendisi tam olarak "bağlı görünüp bağlı olmamak"tı —
/// planlayıcı testleri onu yakalayamazdı.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class TenantCompanyGraphUpdateTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly Tenants.ITenantAppService _tenantAppService;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantManager _tenantManager;
    private readonly IRepository<Company, Guid> _companies;
    private readonly IRepository<CurrencyUnit, Guid> _units;
    private readonly IRepository<Country, Guid> _countries;
    private readonly IdentityUserManager _userManager;
    private readonly IdentityRoleManager _roleManager;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;

    public TenantCompanyGraphUpdateTests()
    {
        _tenantAppService = GetRequiredService<Tenants.ITenantAppService>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _tenantManager    = GetRequiredService<ITenantManager>();
        _companies        = GetRequiredService<IRepository<Company, Guid>>();
        _units            = GetRequiredService<IRepository<CurrencyUnit, Guid>>();
        _countries        = GetRequiredService<IRepository<Country, Guid>>();
        _userManager      = GetRequiredService<IdentityUserManager>();
        _roleManager      = GetRequiredService<IdentityRoleManager>();
        _companyContext   = GetRequiredService<TestCompanyContextProvider>();
        _currentTenant    = GetRequiredService<ICurrentTenant>();
        _dataFilter       = GetRequiredService<IDataFilter>();
    }

    /// <summary>Grafta yapılan ekleme ve ad değişikliği KALICIDIR.</summary>
    [Fact]
    public async Task Update_persists_company_graph_changes()
    {
        var scope = await SeedTenantWithOneCompanyAsync("TGA");

        var graph = await ReadGraphAsync(scope.TenantId);
        var existing = graph.Single();
        existing.Name = "YENİ AD";

        graph.Add(NewCompanyNode(scope, $"{scope.Prefix}-2"));

        await _tenantAppService.UpdateAsync(scope.TenantId, new Tenants.TenantUpdateDto
        {
            Name = scope.TenantName,
            Companies = graph,
        });

        var after = await ReadGraphAsync(scope.TenantId);
        after.Count.ShouldBe(2);
        after.Single(c => c.Id == existing.Id).Name.ShouldBe("YENİ AD");
        after.ShouldContain(c => c.Code == $"{scope.Prefix}-2");
    }

    /// <summary>GRAFI GÖNDERMEYEN çağrı hiçbir şirkete dokunmaz — boş liste "hepsini sil" DEĞİLDİR.
    /// <para>Bu, alan eklendikten sonraki en tehlikeli regresyon: yalnız adı değiştiren bir çağrı (ya da
    /// eski bir istemci) tenant'ın tüm org ağacını silebilirdi.</para></summary>
    [Fact]
    public async Task Update_without_a_graph_leaves_companies_untouched()
    {
        var scope = await SeedTenantWithOneCompanyAsync("TGB");

        await _tenantAppService.UpdateAsync(scope.TenantId, new Tenants.TenantUpdateDto { Name = "SADECE AD" });

        (await ReadGraphAsync(scope.TenantId)).ShouldHaveSingleItem();
    }

    /// <summary>SON MERKEZ silinemez — guard update yolundan da çalışır.
    /// <para>Kural <c>CompanyAppService.DeleteAsync</c>'te TEK yerde durur; bu test onun tenant güncelleme
    /// yolunda da devrede olduğunu pinler (yeni yol eski guard'ı atlamasın).</para></summary>
    [Fact]
    public async Task Update_cannot_delete_the_last_headquarters()
    {
        var scope = await SeedTenantWithOneCompanyAsync("TGC");

        var graph = await ReadGraphAsync(scope.TenantId);
        graph.Single().IsDeleted = true;

        var ex = await Should.ThrowAsync<BusinessException>(
            () => _tenantAppService.UpdateAsync(scope.TenantId, new Tenants.TenantUpdateDto
            {
                Name = scope.TenantName,
                Companies = graph,
            }));

        ex.Code.ShouldBe("TradeXpress:Company:CannotDeleteHeadquarters");
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    private async Task<List<CompanyGraphDto>> ReadGraphAsync(Guid tenantId)
    {
        var dto = await _tenantAppService.GetAsync(tenantId);
        return dto.Companies;
    }

    private static CompanyGraphDto NewCompanyNode(TenantScope scope, string code) => new()
    {
        Id = Guid.Empty,
        Code = code,
        Name = code,
        CountryId = scope.CountryId,
        BaseCurrencyUnitId = scope.CurrencyUnitId,
        IsHeadquarters = false,
        IsActive = true,
    };

    /// <summary>Tenant + "admin" rollü bir kullanıcı (impersonation bunu arar) + tek merkez şirket.
    /// <para>Admin ŞART: graf yazımı <c>CompanyAppService</c>'e delege edilir ve o çağrılar tenant izinleriyle
    /// koşmalıdır. Admin yoksa servis sessizce atlamaz, açık hata verir.</para></summary>
    private async Task<TenantScope> SeedTenantWithOneCompanyAsync(string prefix)
    {
        var suffix = SimpleGuidGenerator.Instance.Create().ToString("N")[..5].ToUpperInvariant();
        var name = $"{prefix}{suffix}";

        var tenant = await WithUnitOfWorkAsync(async () =>
        {
            var created = await _tenantManager.CreateAsync(name);
            await _tenantRepository.InsertAsync(created, autoSave: true);
            return created;
        });

        Guid countryId;
        Guid companyId;

        // TRY HOST kaydıdır (TenantId=null). VoucherTestDataSeeder ile AYNI yol: tenant filtresi kapatılarak
        // koda göre çözülür (ambient tenant ne olursa olsun görünsün). Çözülen id tenant içinde de geçerlidir —
        // filtrenin host muafiyet kolu onu görünür tutar.
        var currencyUnitId = await WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<IMultiTenant>())
            {
                return (await _units.GetAsync(u => u.Code == CurrencyUnitCode.TRY)).Id;
            }
        });

        using (_currentTenant.Change(tenant.Id))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var role = new IdentityRole(SimpleGuidGenerator.Instance.Create(), "admin", tenant.Id);
                (await _roleManager.CreateAsync(role)).Succeeded.ShouldBeTrue();

                var user = new IdentityUser(
                    SimpleGuidGenerator.Instance.Create(), $"admin{suffix}", $"admin{suffix}@example.invalid", tenant.Id);
                (await _userManager.CreateAsync(user, "1q2w3E*")).Succeeded.ShouldBeTrue();
                (await _userManager.AddToRoleAsync(user, "admin")).Succeeded.ShouldBeTrue();
            });

            countryId = await WithUnitOfWorkAsync(async () =>
            {
                var country = new Country(prefix[..2].ToUpperInvariant(), $"{prefix} Country", currencyUnitId);
                await _countries.InsertAsync(country, autoSave: true);
                return country.Id;
            });

            companyId = await WithUnitOfWorkAsync(async () =>
            {
                var company = new Company(
                    $"{name}-1", $"{name} Şirket", countryId, currencyUnitId,
                    isHeadquarters: true, displayOrder: 0);
                await _companies.InsertAsync(company, autoSave: true);
                return company.Id;
            });
        }

        _companyContext.CompanyId = companyId;

        return new TenantScope(tenant.Id, name, name, countryId, currencyUnitId);
    }

    private sealed record TenantScope(
        Guid TenantId, string TenantName, string Prefix, Guid CountryId, Guid CurrencyUnitId);
}
