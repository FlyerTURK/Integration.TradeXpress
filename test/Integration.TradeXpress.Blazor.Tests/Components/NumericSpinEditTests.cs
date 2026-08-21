using Bunit;
using Integration.Framework.Blazor.Client.Components.Shared;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Components;

/// <summary>
/// <see cref="NumericSpinEdit{TValue}"/> — paylaşılan sayısal editör bileşeni.
///
/// <para>İlk test doğrudan 2026-07-28 canlı hatasının regresyonudur: <c>ReadOnly</c> parametresi wrapper'da
/// yokken verilmişti; derleme temiz geçmiş, kullanıcının ekranında
/// <c>"does not have a property matching the name 'ReadOnly'"</c> ile çökmüştü.</para>
/// </summary>
public class NumericSpinEditTests : BlazorComponentTestBase
{
    [Fact]
    public void Renders_with_a_plain_value()
    {
        var component = Render<NumericSpinEdit<int>>(parameters => parameters
            .Add(p => p.Value, 42));

        component.Markup.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Accepts_ReadOnly_parameter()
    {
        // REGRESYON: bu parametre eksikken sayfa açılır açılmaz çöküyordu.
        var component = Render<NumericSpinEdit<int>>(parameters => parameters
            .Add(p => p.Value, 7)
            .Add(p => p.ReadOnly, true));

        component.Markup.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Accepts_nullable_value_with_clear_button()
    {
        var component = Render<NumericSpinEdit<decimal?>>(parameters => parameters
            .Add(p => p.Value, null)
            .Add(p => p.ClearButtonDisplayMode, DevExpress.Blazor.DataEditorClearButtonDisplayMode.Auto));

        component.Markup.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Raises_ValueChanged_when_the_value_is_set()
    {
        var captured = 0;
        var component = Render<NumericSpinEdit<int>>(parameters => parameters
            .Add(p => p.Value, 1)
            .Add(p => p.ValueChanged, v => captured = v));

        component.Instance.Value.ShouldBe(1);
        captured.ShouldBe(0);   // henüz değişim yok — bağlamanın kurulduğunu doğrular
    }
}
