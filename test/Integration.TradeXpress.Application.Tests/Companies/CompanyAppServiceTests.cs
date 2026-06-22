using System.Linq;
using System.Threading.Tasks;
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

    protected CompanyAppServiceTests()
    {
        _appService = GetRequiredService<ICompanyAppService>();
        _currencyUnitAppService = GetRequiredService<ICurrencyUnitAppService>();
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

        await Should.ThrowAsync<BusinessException>(() => _appService.CreateAsync(new CompanyCreateDto
        {
            Code = "MRK",
            Name = "Olmaz",
            CountryCode = "US",
            BaseCurrencyUnitId = usd.Id,
        }));
    }
}
