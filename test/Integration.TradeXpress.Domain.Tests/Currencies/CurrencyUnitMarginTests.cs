using System;
using Integration.TradeXpress.Currencies;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Currencies;

public class CurrencyUnitMarginTests
{
    [Fact]
    public void Defaults_to_passthrough_margins()
    {
        var m = new CurrencyUnitMargin(Guid.NewGuid(), Guid.NewGuid());
        m.MarginOnBuy.ValueEquals(MarginSetting.Passthrough).ShouldBeTrue();
        m.MarginOnSell.ValueEquals(MarginSetting.Passthrough).ShouldBeTrue();
    }

    [Fact]
    public void Ctor_sets_both_margin_legs()
    {
        // Append-only/immutable: marjlar ctor'da set edilir (SetMargins yok).
        var m = new CurrencyUnitMargin(
            Guid.NewGuid(), Guid.NewGuid(),
            new MarginSetting(MarginType.Percent, 2m),
            new MarginSetting(MarginType.Percent, 3m));
        m.MarginOnBuy.Type.ShouldBe(MarginType.Percent);
        m.MarginOnBuy.Value.ShouldBe(2m);
        m.MarginOnSell.Value.ShouldBe(3m);
    }

    [Fact]
    public void Ctor_null_margin_falls_back_to_passthrough()
    {
        var m = new CurrencyUnitMargin(Guid.NewGuid(), Guid.NewGuid(), null, MarginSetting.Passthrough);
        m.MarginOnBuy.ValueEquals(MarginSetting.Passthrough).ShouldBeTrue();
    }

    [Fact]
    public void Ctor_keeps_tenant_and_unit_references()
    {
        var unitId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var m = new CurrencyUnitMargin(Guid.NewGuid(), unitId, tenantId: tenantId);
        m.CurrencyUnitId.ShouldBe(unitId);
        m.TenantId.ShouldBe(tenantId);
    }
}
