using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Scraps;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// YENİ ŞİRKET EMTİA KATALOĞUYLA DOĞAR.
///
/// <para><b>Kapatılan açık:</b> emtia katalogları PER-COMPANY'dir ve seeder'lar bunu doğru yapıyordu — ama
/// yalnız <c>TradeXpressDataSeedContributor</c>'dan (DbMigrator / tenant onboarding'in ikinci pass'i)
/// tetikleniyorlardı. <c>CompanyAppService.CreateAsync</c> hiçbir seeder çağırmıyordu → Şirketler ekranından
/// açılan şirket emtia kataloglarında BOŞ doğuyordu.</para>
///
/// <para><b>Hata neden sessiz:</b> hiçbir istisna yok, hiçbir log yok. Şirket açılır, emtia listeleri boş gelir
/// ve kullanıcı "henüz girmedim" sanır. Reçete kurmaya çalıştığında seçecek maden bulamaz — o noktada da
/// sebebi şirketin kuruluşuna bağlamak neredeyse imkânsızdır.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class CompanyCatalogSeedingTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ICompanyAppService _companyAppService;
    private readonly IRepository<Metal, Guid> _metals;
    private readonly IRepository<Scrap, Guid> _scraps;
    private readonly IRepository<Future, Guid> _futures;
    private readonly IRepository<CurrencyUnit, Guid> _units;
    private readonly IRepository<Country, Guid> _countries;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;

    public CompanyCatalogSeedingTests()
    {
        _companyAppService = GetRequiredService<ICompanyAppService>();
        _metals            = GetRequiredService<IRepository<Metal, Guid>>();
        _scraps            = GetRequiredService<IRepository<Scrap, Guid>>();
        _futures           = GetRequiredService<IRepository<Future, Guid>>();
        _units             = GetRequiredService<IRepository<CurrencyUnit, Guid>>();
        _countries         = GetRequiredService<IRepository<Country, Guid>>();
        _companyContext    = GetRequiredService<TestCompanyContextProvider>();
        _currentTenant     = GetRequiredService<ICurrentTenant>();
        _dataFilter        = GetRequiredService<IDataFilter>();
    }

    /// <summary>Şirketler ekranından açılan şirket sistem kataloglarını alır (Maden · Hurda · Vadeli).</summary>
    [Fact]
    public async Task Company_created_through_the_app_service_gets_its_commodity_catalogs()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var suffix = SimpleGuidGenerator.Instance.Create().ToString("N")[..5].ToUpperInvariant();

        using (_currentTenant.Change(tenantId))
        {
            var (countryId, currencyUnitId) = await SeedReferenceDataAsync(suffix);
            _companyContext.CompanyId = null;

            var created = await _companyAppService.CreateAsync(new CompanyCreateDto
            {
                Code = $"CAT{suffix}",
                Name = $"Katalog {suffix}",
                CountryId = countryId,
                BaseCurrencyUnitId = currencyUnitId,
                IsHeadquarters = true,
                Branches = new List<Branches.BranchGraphDto>(),
            });

            await CountsOf(created.Id).ShouldHaveSeededCatalogsAsync();
        }
    }

    /// <summary>İKİNCİ çağrı katalogları ÇOĞALTMAZ — seeder idempotent, tetikleyici de öyle kalmalı.
    /// <para>Çağrı mevcut tüm şirketleri dolaştığı için bu davranış şart: her şirket açılışında kataloglar
    /// yeniden yazılsaydı ikinci şirketi açmak birincininkini ikiye katlardı.</para></summary>
    [Fact]
    public async Task Creating_a_second_company_does_not_duplicate_the_first_ones_catalog()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var suffix = SimpleGuidGenerator.Instance.Create().ToString("N")[..5].ToUpperInvariant();

        using (_currentTenant.Change(tenantId))
        {
            var (countryId, currencyUnitId) = await SeedReferenceDataAsync(suffix);
            _companyContext.CompanyId = null;

            var first = await _companyAppService.CreateAsync(new CompanyCreateDto
            {
                Code = $"DUP{suffix}A", Name = $"Dup A {suffix}",
                CountryId = countryId, BaseCurrencyUnitId = currencyUnitId,
                IsHeadquarters = true, Branches = new List<Branches.BranchGraphDto>(),
            });

            var beforeMetals = await CountAsync(_metals, first.Id);
            beforeMetals.ShouldBeGreaterThan(0);

            await _companyAppService.CreateAsync(new CompanyCreateDto
            {
                Code = $"DUP{suffix}B", Name = $"Dup B {suffix}",
                CountryId = countryId, BaseCurrencyUnitId = currencyUnitId,
                IsHeadquarters = false, Branches = new List<Branches.BranchGraphDto>(),
            });

            (await CountAsync(_metals, first.Id)).ShouldBe(beforeMetals);
        }
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    private CatalogCounts CountsOf(Guid companyId) => new(this, companyId);

    private async Task<int> CountAsync<TEntity>(IRepository<TEntity, Guid> repository, Guid companyId)
        where TEntity : class, Volo.Abp.Domain.Entities.IEntity<Guid>, ICompanyOwned
    {
        return await WithUnitOfWorkAsync(() => repository.CountAsync(e => e.CompanyId == companyId));
    }

    /// <summary>Şirket kurulumunun ihtiyaç duyduğu görünür ülke + para birimi.
    /// <para>TRY HOST kaydıdır (TenantId=null) → tenant filtresi kapatılarak çözülür; çözülen id tenant içinde
    /// de görünür (filtrenin host muafiyet kolu).</para></summary>
    private async Task<(Guid CountryId, Guid CurrencyUnitId)> SeedReferenceDataAsync(string suffix)
    {
        var currencyUnitId = await WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<IMultiTenant>())
            {
                return (await _units.GetAsync(u => u.Code == CurrencyUnitCode.TRY)).Id;
            }
        });

        var countryId = await WithUnitOfWorkAsync(async () =>
        {
            var country = new Country(suffix[..2], $"Ülke {suffix}", currencyUnitId);
            await _countries.InsertAsync(country, autoSave: true);
            return country.Id;
        });

        return (countryId, currencyUnitId);
    }

    /// <summary>Seed VERİSİ OLAN katalogların dolduğunu tek yerde iddia eder — biri eksik kalırsa hangisi
    /// olduğu mesajdan görünür.
    ///
    /// <para><b>Hizmet BİLİNÇLİ olarak dışarıda:</b> <c>ServiceSeeder.Seeds</c> boş bir dizidir
    /// (<i>"gerçek hizmet listesi netleşince doldurulacak — fake örnek veri konmaz"</i>). Seeder koşar ama
    /// yazacak bir şey bulmaz; sıfır satır burada HATA DEĞİL, doğru davranıştır. Listeye gerçek hizmetler
    /// eklendiğinde bu satır da açılmalı.</para></summary>
    private sealed class CatalogCounts(CompanyCatalogSeedingTests owner, Guid companyId)
    {
        public async Task ShouldHaveSeededCatalogsAsync()
        {
            (await owner.CountAsync(owner._metals, companyId)).ShouldBeGreaterThan(0, "Maden kataloğu boş");
            (await owner.CountAsync(owner._scraps, companyId)).ShouldBeGreaterThan(0, "Hurda kataloğu boş");
            (await owner.CountAsync(owner._futures, companyId)).ShouldBeGreaterThan(0, "Vadeli kataloğu boş");
        }
    }
}
