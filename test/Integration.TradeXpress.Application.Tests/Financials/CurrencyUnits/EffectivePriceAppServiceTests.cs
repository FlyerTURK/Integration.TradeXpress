using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

public abstract class EffectivePriceAppServiceTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IEffectivePriceAppService _appService;

    protected EffectivePriceAppServiceTests()
    {
        _appService = GetRequiredService<IEffectivePriceAppService>();
    }

    [Fact]
    public async Task Host_current_prices_include_seeded_TRY_at_one()
    {
        var prices = await _appService.GetCurrentPricesAsync();

        // Seed yalnız TRY için host ham ExchangeRate (1/1) yazar → en az TRY gelir.
        var tryPrice = prices.SingleOrDefault(p => p.CurrencyUnitCode == CurrencyUnitCode.TRY);
        tryPrice.ShouldNotBeNull();

        // TRY margin = FinalPrice(1) → host efektifi 1/1 (ham 1/1 üstüne).
        tryPrice!.Buy.ShouldBe(1m);
        tryPrice.Sell.ShouldBe(1m);
        tryPrice.RawBuy.ShouldBe(1m);
        tryPrice.GuardFired.ShouldBeFalse();
    }

    [Fact]
    public async Task Units_without_a_raw_rate_default_to_one()
    {
        // Tasarım (EffectivePriceAppService): fiyat kaynağı (feed/rate/takip) olmayan birim listede
        // BOŞ geçmez → ham 1/1 varsayılanıyla görünür. Seed yalnız TRY'ye ham rate yazar.
        var prices = await _appService.GetCurrentPricesAsync();

        // USD seed'li ama ham rate'i yok → omit DEĞİL; ham 1/1 ile listede yer alır (marjdan bağımsız).
        var usd = prices.SingleOrDefault(p => p.CurrencyUnitCode == CurrencyUnitCode.USD);
        usd.ShouldNotBeNull();
        usd!.RawBuy.ShouldBe(1m);
        usd.RawSell.ShouldBe(1m);
    }

    [Fact]
    public async Task Valuation_with_TR_headquarters_is_identity()
    {
        // HQ = TR/TRY → base=TRY; TRY efektifi 1/1, kendi base'ine re-base → 1/1 (identity).
        var valuation = await _appService.GetValuationAsync(); // companyId yok → HQ

        var tryV = valuation.Single(p => p.CurrencyUnitCode == CurrencyUnitCode.TRY);
        tryV.Buy.ShouldBe(1m);
        tryV.Sell.ShouldBe(1m);
        tryV.BaseCurrencyCode.ShouldBe(CurrencyUnitCode.TRY);
    }
}
