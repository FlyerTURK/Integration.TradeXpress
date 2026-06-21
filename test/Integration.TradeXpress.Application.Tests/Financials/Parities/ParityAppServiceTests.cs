using System.Threading.Tasks;
using Integration.TradeXpress.Financials.Parities;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Financials.Parities;

public abstract class ParityAppServiceTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IParityAppService _appService;

    protected ParityAppServiceTests()
    {
        _appService = GetRequiredService<IParityAppService>();
    }

    [Fact]
    public async Task GetList_returns_seeded_global_parities()
    {
        // Host birimlerinden C(n,2) parite seed'li → liste dolu döner (crash yok).
        var list = await _appService.GetListAsync(new ParityListRequestDto { MaxResultCount = 1000 });

        list.ShouldNotBeNull();
        list.TotalCount.ShouldBeGreaterThan(0);
        list.Items.ShouldAllBe(p => p.IsGlobal);
    }

    [Fact]
    public async Task Create_rejects_same_and_reverse_pair()
    {
        var existing = (await _appService.GetListAsync(new ParityListRequestDto { MaxResultCount = 1 })).Items[0];

        // Aynı çift → PairAlreadyExists.
        await Should.ThrowAsync<BusinessException>(() => _appService.CreateAsync(new ParityCreateDto
        {
            BaseCurrencyUnitId = existing.BaseCurrencyUnitId,
            QuoteCurrencyUnitId = existing.QuoteCurrencyUnitId,
        }));

        // Ters çift (USDTRY → TRYUSD) → ReversePairAlreadyExists.
        await Should.ThrowAsync<BusinessException>(() => _appService.CreateAsync(new ParityCreateDto
        {
            BaseCurrencyUnitId = existing.QuoteCurrencyUnitId,
            QuoteCurrencyUnitId = existing.BaseCurrencyUnitId,
        }));
    }
}
