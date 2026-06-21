using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Countries;

public abstract class CountryAppServiceTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ICountryAppService _appService;

    protected CountryAppServiceTests()
    {
        _appService = GetRequiredService<ICountryAppService>();
    }

    [Fact]
    public async Task Host_should_have_seeded_country_catalog()
    {
        var list = await _appService.GetListAsync(new CountryListRequestDto { MaxResultCount = 100 });

        list.TotalCount.ShouldBeGreaterThanOrEqualTo(10);

        var tr = list.Items.Single(c => c.Code == "TR");
        tr.Name.ShouldBe("Türkiye");
        tr.DefaultCurrencyCode.ShouldBe(CurrencyUnitCode.TRY);
        tr.IsGlobal.ShouldBeTrue();

        var us = list.Items.Single(c => c.Code == "US");
        us.DefaultCurrencyCode.ShouldBe(CurrencyUnitCode.USD);
    }
}
