using System;
using Integration.TradeXpress.Currencies;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Currencies;

public class CurrencyUnitTests
{
    private static CurrencyUnit New(bool system = false)
        => new(Guid.NewGuid(), "USD", "US Dollar", CurrencyUnitType.Cash, isSystem: system);

    [Fact]
    public void System_unit_code_is_immutable()
        => Should.Throw<InvalidOperationException>(() => New(system: true).SetCode("EUR"));

    [Fact]
    public void Non_system_unit_code_can_change()
    {
        var u = New(system: false);
        u.SetCode("EUR");
        u.Code.ShouldBe("EUR");
    }

    [Fact]
    public void Cannot_follow_itself()
    {
        var u = New();
        Should.Throw<InvalidOperationException>(() => u.SetFollowing(u.Id, MarginSetting.Passthrough));
    }

    [Fact]
    public void Following_requires_margin()
        => Should.Throw<ArgumentNullException>(() => New().SetFollowing(Guid.NewGuid(), null));

    [Fact]
    public void Setting_then_clearing_following_wipes_margin()
    {
        var u = New();
        u.SetFollowing(Guid.NewGuid(), new MarginSetting(MarginType.Percent, 1m));
        u.IsFollowing.ShouldBeTrue();
        u.FollowingMargin.ShouldNotBeNull();

        u.SetFollowing(null, null);
        u.IsFollowing.ShouldBeFalse();
        u.FollowingMargin.ShouldBeNull();
    }

    [Fact]
    public void Activate_deactivate_toggles()
    {
        var u = New();
        u.IsActive.ShouldBeTrue();   // ctor default
        u.Deactivate();
        u.IsActive.ShouldBeFalse();
        u.Activate();
        u.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void New_unit_is_not_following_by_default()
    {
        var u = New();
        u.IsFollowing.ShouldBeFalse();
    }
}
