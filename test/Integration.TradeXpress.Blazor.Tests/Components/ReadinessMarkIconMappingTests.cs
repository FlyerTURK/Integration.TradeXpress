using System;
using System.Collections.Generic;
using Bunit;
using Integration.TradeXpress.Blazor.Client;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Products;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Components;

/// <summary>
/// İKON EŞLEMESİ TEK YERDE (2026-08-19). İşaret bileşeni ikonu ağırlıktan TÜRETİR; sabit tek bir ikon çizmek
/// (bir dönem çizdiği gibi) aynı Error issue'sunun satışa hazırlık paneli listesinde "dur", grid satırında "ünlem" görünmesine
/// yol açıyordu — kullanıcı iki ekranı aynı olay hakkında farklı ciddiyette okuyor.
///
/// <para>Bu testler o sapmanın geri gelmesini engeller: eşleme paletten çıkar ve işaret onu kullanır.</para>
/// </summary>
public class ReadinessMarkIconMappingTests : BlazorComponentTestBase
{
    private const string Scope = "variants/33333333-3333-3333-3333-333333333333";

    [Fact]
    public void The_palette_maps_each_severity_to_one_icon()
    {
        SaleReadinessPalette.IconOf(SaleReadinessSeverity.Error).ShouldBe(TradeXpressIcons.Close);
        SaleReadinessPalette.IconOf(SaleReadinessSeverity.Warning).ShouldBe(TradeXpressIcons.Warning);
        SaleReadinessPalette.IconOf(SaleReadinessSeverity.Info).ShouldBe(TradeXpressIcons.Lightbulb);

        // Ağırlık = "issue yok" da bilgi ikonuna düşer; işaret zaten çizilmez (IsActionable false).
        SaleReadinessPalette.IconOf(null).ShouldBe(TradeXpressIcons.Lightbulb);
    }

    [Fact]
    public void A_blocking_scope_is_drawn_with_the_blocking_icon()
    {
        var cut = Render<ReadinessMark>(parameters => parameters
            .Add(p => p.Index, IndexWith(SaleReadinessSeverity.Error))
            .Add(p => p.Scope, Scope));

        cut.Markup.ShouldContain(TradeXpressIcons.Close);
    }

    [Fact]
    public void A_warning_scope_is_drawn_with_the_warning_icon()
    {
        var cut = Render<ReadinessMark>(parameters => parameters
            .Add(p => p.Index, IndexWith(SaleReadinessSeverity.Warning))
            .Add(p => p.Scope, Scope));

        cut.Markup.ShouldContain(TradeXpressIcons.Warning);
        cut.Markup.ShouldNotContain(TradeXpressIcons.Close);
    }

    private static SaleReadinessIssueIndex IndexWith(SaleReadinessSeverity severity)
    {
        return new SaleReadinessIssueIndex(new List<SaleReadinessIssueDto>
        {
            new()
            {
                Severity = severity,
                Path = Scope,
                Message = "test",
                Code = "Test:Issue",
                TargetId = Guid.Empty,
            },
        });
    }
}
