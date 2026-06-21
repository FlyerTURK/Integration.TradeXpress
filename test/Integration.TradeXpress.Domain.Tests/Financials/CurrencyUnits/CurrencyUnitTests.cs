using Integration.Framework;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

public class CurrencyUnitTests
{
    private static CurrencyUnit New()
    {
        return new CurrencyUnit("USD", "US Dollar", CurrencyUnitType.Cash);
    }

    [Fact]
    public void Cannot_follow_itself()
    {
        var u = New();
        Should.Throw<BusinessException>(() => u.SetFollowing(u.Id, MarginSetting.Passthrough));
    }

    [Fact]
    public void Following_requires_margin()
    {
        Should.Throw<RequiredPropertyException>(
            () => New().SetFollowing(SimpleGuidGenerator.Instance.Create(), null));
    }

    [Fact]
    public void Setting_then_clearing_following_wipes_margin()
    {
        var u = New();
        u.SetFollowing(SimpleGuidGenerator.Instance.Create(), new MarginSetting(MarginType.Percent, 1m));
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
        u.SetActive(false);
        u.IsActive.ShouldBeFalse();
        u.SetActive(true);
        u.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void New_unit_is_not_following_by_default()
    {
        var u = New();
        u.IsFollowing.ShouldBeFalse();
    }
}
