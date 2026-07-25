using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Dtos;
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
        // AllPages: katalogun TAMAMI. Eskiden MaxResultCount=100 yazıyordu ve ApplyListRequest 200'e kırptığı
        // için 249 ülkenin yalnız bir kısmı dönüyordu → alfabetik olarak sona düşen ülkeler (US dahil) testte
        // "bulunamadı" veriyordu. Sayı yazmak yerine niyeti yazıyoruz.
        var list = await _appService.GetListAsync(new CountryListRequestDto { MaxResultCount = ListRequestDto.AllPages });

        list.TotalCount.ShouldBeGreaterThanOrEqualTo(10);

        var tr = list.Items.Single(c => c.Code == "TR");
        tr.Name.ShouldBe("Türkiye");
        tr.DefaultCurrencyUnitId.ShouldNotBeNull();                   // id-only otoriter alan (FK) dolu
        tr.DefaultCurrencyCode.ShouldBe(CurrencyUnitCode.TRY);        // görüntü kodu id'den çözülür
        tr.IsGlobal.ShouldBeTrue();

        var us = list.Items.Single(c => c.Code == "US");
        us.DefaultCurrencyUnitId.ShouldNotBeNull();
        us.DefaultCurrencyCode.ShouldBe(CurrencyUnitCode.USD);
    }
}
