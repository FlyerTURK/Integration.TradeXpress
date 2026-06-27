using Integration.TradeXpress.Financials.CurrencyUnits;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

public class CurrencyUnitMarginTests
{
    [Fact]
    public void Defaults_to_passthrough_margins()
    {
        var m = new CurrencyUnitMargin(SimpleGuidGenerator.Instance.Create(), companyId: null);
        m.MarginOnBuy.ValueEquals(MarginSetting.Passthrough).ShouldBeTrue();
        m.MarginOnSell.ValueEquals(MarginSetting.Passthrough).ShouldBeTrue();
    }

    [Fact]
    public void Ctor_sets_both_margin_legs()
    {
        // Append-only/immutable: marjlar ctor'da set edilir (SetMargins yok).
        var m = new CurrencyUnitMargin(
            SimpleGuidGenerator.Instance.Create(),
            companyId: null,
            new MarginSetting(MarginType.Percent, 2m),
            new MarginSetting(MarginType.Percent, 3m));
        m.MarginOnBuy.Type.ShouldBe(MarginType.Percent);
        m.MarginOnBuy.Value.ShouldBe(2m);
        m.MarginOnSell.Value.ShouldBe(3m);
    }

    [Fact]
    public void Ctor_null_margin_falls_back_to_passthrough()
    {
        var m = new CurrencyUnitMargin(
            SimpleGuidGenerator.Instance.Create(), companyId: null, marginOnBuy: null, marginOnSell: MarginSetting.Passthrough);
        m.MarginOnBuy.ValueEquals(MarginSetting.Passthrough).ShouldBeTrue();
    }

    [Fact]
    public void Ctor_keeps_unit_reference()
    {
        // TenantId artık ABP'nin CurrentTenant'tan atadığı bir alan (ctor'da yok) →
        // saf unit test'te yalnız CurrencyUnitId doğrulanır.
        var unitId = SimpleGuidGenerator.Instance.Create();
        var m = new CurrencyUnitMargin(unitId, companyId: null);
        m.CurrencyUnitId.ShouldBe(unitId);
    }
}
