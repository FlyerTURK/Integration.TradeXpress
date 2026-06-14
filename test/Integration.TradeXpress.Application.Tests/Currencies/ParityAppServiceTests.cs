using System.Threading.Tasks;
using Integration.TradeXpress.Currencies;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Currencies;

public abstract class ParityAppServiceTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IParityAppService _appService;

    protected ParityAppServiceTests()
    {
        _appService = GetRequiredService<IParityAppService>();
    }

    [Fact]
    public async Task Board_runs_and_is_empty_without_feed()
    {
        // Pariteler seed'li (66) ama feed yok → fiyatı olan tek birim TRY.
        // Bir parite base+quote İKİSİ de fiyatlı ister → board boş (crash yok).
        var board = await _appService.GetBoardAsync();
        board.ShouldNotBeNull();
        board.ShouldBeEmpty();
    }
}
