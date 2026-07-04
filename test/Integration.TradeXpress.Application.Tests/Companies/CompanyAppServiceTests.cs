using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Companies;

// NOT: Eski SaveTree/tree-DTO testleri kaldırıldı — şirket+şube+kasa grafı artık standart Create/Update
// üzerinden taşınıp BranchAppService'e (o da VaultAppService'e) delege ediliyor. Graf senaryoları için
// yeni testler ileride eklenecek.
public abstract class CompanyAppServiceTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ICompanyAppService _appService;
    private readonly ICurrencyUnitAppService _currencyUnitAppService;
    private readonly ICountryAppService _countryAppService;

    protected CompanyAppServiceTests()
    {
        _appService = GetRequiredService<ICompanyAppService>();
        _currencyUnitAppService = GetRequiredService<ICurrencyUnitAppService>();
        _countryAppService = GetRequiredService<ICountryAppService>();
    }

    [Fact]
    public async Task Host_has_no_companies()
    {
        // Şirket/şube TENANT'a aittir; host (merkezi operasyon) şirket tutmaz.
        var list = await _appService.GetListAsync(new CompanyListRequestDto { MaxResultCount = 100 });
        list.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Host_cannot_create_a_company()
    {
        var usd = (await _currencyUnitAppService.GetListAsync(new CurrencyUnitListRequestDto { Filter = "USD" }))
            .Items.Single(u => u.Code == CurrencyUnitCode.USD);
        // Country id-only geçişi: DTO artık kod değil Country.Id taşır (katalogdan çözülür).
        var us = (await _countryAppService.GetListAsync(new CountryListRequestDto { MaxResultCount = 100 }))
            .Items.Single(c => c.Code == "US");

        await Should.ThrowAsync<BusinessException>(() => _appService.CreateAsync(new CompanyCreateDto
        {
            Code = "MRK",
            Name = "Olmaz",
            CountryId = us.Id,
            BaseCurrencyUnitId = usd.Id,
        }));
    }
}
