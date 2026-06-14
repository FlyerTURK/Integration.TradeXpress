using System;
using Integration.TradeXpress.Currencies;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Currencies;

public class ExchangeRateTests
{
    private static ExchangeRate Create(decimal buy, decimal sell) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        buy, sell,
        MarginSetting.Passthrough,
        MarginSetting.Passthrough,
        source: "Test",
        rateDate: DateTime.UtcNow);

    [Fact]
    public void Positive_prices_are_accepted()
    {
        var r = Create(40m, 41m);
        r.MarketPriceOnBuy.ShouldBe(40m);
        r.MarketPriceOnSell.ShouldBe(41m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_buy_is_rejected(decimal buy)
        => Should.Throw<ArgumentOutOfRangeException>(() => Create(buy, 41m));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_sell_is_rejected(decimal sell)
        => Should.Throw<ArgumentOutOfRangeException>(() => Create(40m, sell));
}
