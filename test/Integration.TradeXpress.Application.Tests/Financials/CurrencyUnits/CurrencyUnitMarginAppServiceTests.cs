using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

public abstract class CurrencyUnitMarginAppServiceTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ICurrencyUnitMarginAppService _appService;

    protected CurrencyUnitMarginAppServiceTests()
    {
        _appService = GetRequiredService<ICurrencyUnitMarginAppService>();
    }

    [Fact]
    public async Task Host_should_have_a_seeded_margin_per_unit()
    {
        var result = await _appService.GetListAsync(new CurrencyUnitMarginListRequestDto { MaxResultCount = 100 });

        result.TotalCount.ShouldBe(12);
        var tryRow = result.Items.Single(m => m.CurrencyUnitCode == CurrencyUnitCode.TRY);
        tryRow.MarginOnBuyType.ShouldBe(MarginType.FinalPrice);
        tryRow.MarginOnBuyValue.ShouldBe(1m);

        var usd = result.Items.Single(m => m.CurrencyUnitCode == CurrencyUnitCode.USD);
        usd.MarginOnBuyType.ShouldBe(MarginType.Multiply);
        usd.MarginOnBuyValue.ShouldBe(1m);
    }

    [Fact]
    public async Task Set_inserts_new_row_and_becomes_current()
    {
        var usd = (await _appService.GetListAsync(new CurrencyUnitMarginListRequestDto { Filter = "USD" }))
            .Items.Single(m => m.CurrencyUnitCode == CurrencyUnitCode.USD);

        var set = await _appService.SetAsync(new CurrencyUnitMarginSetDto
        {
            CurrencyUnitId = usd.CurrencyUnitId,
            MarginOnBuyType = MarginType.Percent, MarginOnBuyValue = 2m,
            MarginOnSellType = MarginType.Percent, MarginOnSellValue = 4m,
        });

        set.MarginOnBuyType.ShouldBe(MarginType.Percent);
        set.CurrencyUnitCode.ShouldBe(CurrencyUnitCode.USD); // join korunur

        // Güncel liste artık yeni marjı gösterir (latest/unit) ve hâlâ 12 birim (tek satır/birim).
        var list = await _appService.GetListAsync(new CurrencyUnitMarginListRequestDto { MaxResultCount = 100 });
        list.TotalCount.ShouldBe(12);
        var current = list.Items.Single(m => m.CurrencyUnitCode == CurrencyUnitCode.USD);
        current.MarginOnBuyType.ShouldBe(MarginType.Percent);
        current.MarginOnBuyValue.ShouldBe(2m);
    }

    [Fact]
    public async Task Set_appends_history_without_deleting()
    {
        var usd = (await _appService.GetListAsync(new CurrencyUnitMarginListRequestDto { Filter = "USD" }))
            .Items.Single(m => m.CurrencyUnitCode == CurrencyUnitCode.USD);

        await _appService.SetAsync(new CurrencyUnitMarginSetDto
        {
            CurrencyUnitId = usd.CurrencyUnitId,
            MarginOnBuyType = MarginType.Percent, MarginOnBuyValue = 2m,
            MarginOnSellType = MarginType.Percent, MarginOnSellValue = 4m,
        });

        var history = await _appService.GetHistoryAsync(usd.CurrencyUnitId);
        // Seed satırı + 1 set = en az 2; en yeni önce.
        history.Count.ShouldBeGreaterThanOrEqualTo(2);
        history.First().MarginOnBuyType.ShouldBe(MarginType.Percent);
    }
}
