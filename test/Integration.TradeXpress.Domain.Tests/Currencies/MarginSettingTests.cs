using Integration.TradeXpress.Currencies;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Currencies;

public class MarginSettingTests
{
    [Fact]
    public void Multiply_scales_market_price()
        => new MarginSetting(MarginType.Multiply, 1.02m).Apply(100m).ShouldBe(102m);

    [Fact]
    public void Amount_adds_to_market_price()
        => new MarginSetting(MarginType.Amount, 5m).Apply(100m).ShouldBe(105m);

    [Fact]
    public void Percent_applies_markup_percent()
        => new MarginSetting(MarginType.Percent, 2m).Apply(100m).ShouldBe(102m);

    [Fact]
    public void FinalPrice_ignores_market_and_returns_value()
        => new MarginSetting(MarginType.FinalPrice, 16000m).Apply(13000m).ShouldBe(16000m);

    [Fact]
    public void Passthrough_returns_market_unchanged()
        => MarginSetting.Passthrough.Apply(42.5m).ShouldBe(42.5m);

    [Fact]
    public void Value_object_equality_by_components()
    {
        // ABP ValueObject Equals'i override etmez (EF tracking için) → ValueEquals kullanılır.
        var a = new MarginSetting(MarginType.Multiply, 1m);
        var b = new MarginSetting(MarginType.Multiply, 1m);
        var c = new MarginSetting(MarginType.Multiply, 2m);

        a.ValueEquals(b).ShouldBeTrue();
        a.ValueEquals(c).ShouldBeFalse();
    }
}
