using System;
using System.Collections.Generic;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Products;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Pages;

/// <summary>
/// ISSUE ENDEKSİNİN SÖZLEŞMESİ (2026-08-19 Hakan kuralı: "bulgu bulunduğu HER seviyede görünsün; her bölüm o
/// bölümün EN YÜKSEK ağırlığıyla renklensin").
///
/// <para>Endeks, kuralın TEK uygulamasıdır: hangi sekmenin hangi issue'yu göstereceği UI'da kurallaşmaz, yalnız
/// yol ön-eki kıyaslanır. Bu yüzden kilitlenmesi gerekenler: (1) üst kapsam alt issue'yu GÖRÜR; (2) kıyas SEGMENT
/// SINIRINDA durur — <c>variants</c> kapsamı <c>variantsummary</c>'yi kapsamaz (aksi hâlde birbiriyle ilgisiz iki
/// bölüm birbirinin rengini alırdı ve hata gözle görülmezdi); (3) Info sayaca girmez (rozet şişmesin, KDV
/// issue'su hiçbir başlığı boyamasın); (4) engel/uyarı sayıları AYRI okunabilir (uyarı bandı "N engel · M uyarı"
/// diyor).</para>
/// </summary>
public class SaleReadinessIssueIndexTests
{
    private static readonly Guid VariantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherVariantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ChannelProductId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void An_empty_index_colours_nothing()
    {
        SaleReadinessIssueIndex.Empty.MaxSeverity(null).ShouldBeNull();
        SaleReadinessIssueIndex.Empty.Count(null).ShouldBe(0);
        SaleReadinessIssueIndex.Empty.HasError(null).ShouldBeFalse();
        SaleReadinessIssueIndex.Empty.For(SaleReadinessScope.Variants).ShouldBeEmpty();
    }

    [Fact]
    public void A_parent_scope_sees_the_severity_of_a_finding_nested_below_it()
    {
        var index = Build(
            Issue(SaleReadinessSeverity.Error, SaleReadinessScope.VariantRecipe(VariantId)));

        // Issue en derin yoldadır; ama varyant satırı, Varyantlar sekmesi ve kök de onu görmelidir.
        index.MaxSeverity(SaleReadinessScope.VariantRecipe(VariantId)).ShouldBe(SaleReadinessSeverity.Error);
        index.MaxSeverity(SaleReadinessScope.Variant(VariantId)).ShouldBe(SaleReadinessSeverity.Error);
        index.MaxSeverity(SaleReadinessScope.Variants).ShouldBe(SaleReadinessSeverity.Error);
        index.MaxSeverity(null).ShouldBe(SaleReadinessSeverity.Error);

        // ...başka bir varyant ise ETKİLENMEZ.
        index.MaxSeverity(SaleReadinessScope.Variant(OtherVariantId)).ShouldBeNull();
        index.MaxSeverity(SaleReadinessScope.Media).ShouldBeNull();
    }

    [Fact]
    public void A_scope_reports_the_highest_severity_it_contains()
    {
        var index = Build(
            Issue(SaleReadinessSeverity.Info, SaleReadinessScope.Variant(VariantId)),
            Issue(SaleReadinessSeverity.Warning, SaleReadinessScope.Variant(VariantId)),
            Issue(SaleReadinessSeverity.Error, SaleReadinessScope.VariantRecipe(VariantId)));

        index.MaxSeverity(SaleReadinessScope.Variant(VariantId)).ShouldBe(SaleReadinessSeverity.Error);
        index.HasError(SaleReadinessScope.Variant(VariantId)).ShouldBeTrue();
    }

    [Fact]
    public void Prefix_matching_stops_at_a_segment_boundary()
    {
        // "variantsummary" yolu "variants" kapsamıyla AYNI harflerle başlar ama başka bir bölümdür.
        var index = Build(Issue(SaleReadinessSeverity.Error, "variantsummary/x"));

        index.MaxSeverity(SaleReadinessScope.Variants).ShouldBeNull();
        index.MaxSeverity(null).ShouldBe(SaleReadinessSeverity.Error);
    }

    [Fact]
    public void Info_findings_never_reach_the_badge_counter()
    {
        // KDV issue'su Info'dur ve hiçbir başlığı renklendirmez (2026-08-19 Hakan kararı).
        var index = Build(
            Issue(SaleReadinessSeverity.Info, SaleReadinessScope.General),
            Issue(SaleReadinessSeverity.Warning, SaleReadinessScope.Variant(VariantId)));

        index.Count(null).ShouldBe(1);
        index.Count(SaleReadinessScope.General).ShouldBe(0);
        SaleReadinessPalette.IsActionable(index.MaxSeverity(SaleReadinessScope.General)).ShouldBeFalse();
        SaleReadinessPalette.HeadingColorOf(index.MaxSeverity(SaleReadinessScope.General)).ShouldBeNull();
    }

    [Fact]
    public void Blockers_and_warnings_are_counted_separately_for_the_banner()
    {
        var index = Build(
            Issue(SaleReadinessSeverity.Error, SaleReadinessScope.ChannelVariantRecipe(ChannelProductId, VariantId)),
            Issue(SaleReadinessSeverity.Error, SaleReadinessScope.Variant(VariantId)),
            Issue(SaleReadinessSeverity.Warning, SaleReadinessScope.Media),
            Issue(SaleReadinessSeverity.Info, SaleReadinessScope.General));

        index.CountOf(SaleReadinessSeverity.Error, null).ShouldBe(2);
        index.CountOf(SaleReadinessSeverity.Warning, null).ShouldBe(1);
        index.CountOf(SaleReadinessSeverity.Error, SaleReadinessScope.Channels).ShouldBe(1);
        index.CountOf(SaleReadinessSeverity.Error, SaleReadinessScope.Media).ShouldBe(0);
    }

    [Fact]
    public void The_channel_scenario_lights_up_every_level_it_passes_through()
    {
        // Hakan'ın örnek senaryosu: "kanal ürünü var ama varyantlara temel emtia eklenmemiş".
        var index = Build(Issue(
            SaleReadinessSeverity.Error,
            SaleReadinessScope.ChannelVariantRecipe(ChannelProductId, VariantId)));

        index.HasError(SaleReadinessScope.Channels).ShouldBeTrue();
        index.HasError(SaleReadinessScope.Channel(ChannelProductId)).ShouldBeTrue();
        index.HasError(SaleReadinessScope.ChannelVariants(ChannelProductId)).ShouldBeTrue();
        index.HasError(SaleReadinessScope.ChannelVariant(ChannelProductId, VariantId)).ShouldBeTrue();
        index.HasError(SaleReadinessScope.ChannelVariantRecipe(ChannelProductId, VariantId)).ShouldBeTrue();

        // Core (ürünün kendi) Varyantlar sekmesi kanal yolundaki issue'yu ÜSTLENMEZ — o kanalın kendi kaydının sorunudur.
        index.HasError(SaleReadinessScope.Variants).ShouldBeFalse();
    }

    [Fact]
    public void For_returns_the_findings_of_the_scope_in_server_order()
    {
        var first = Issue(SaleReadinessSeverity.Error, SaleReadinessScope.Variant(VariantId), "first");
        var second = Issue(SaleReadinessSeverity.Warning, SaleReadinessScope.VariantRecipe(VariantId), "second");
        var elsewhere = Issue(SaleReadinessSeverity.Error, SaleReadinessScope.Media, "elsewhere");

        var index = Build(first, second, elsewhere);

        var scoped = index.For(SaleReadinessScope.Variant(VariantId));
        scoped.Count.ShouldBe(2);
        scoped[0].Message.ShouldBe("first");
        scoped[1].Message.ShouldBe("second");
    }

    private static SaleReadinessIssueIndex Build(params SaleReadinessIssueDto[] issues)
    {
        return new SaleReadinessIssueIndex(new List<SaleReadinessIssueDto>(issues));
    }

    private static SaleReadinessIssueDto Issue(SaleReadinessSeverity severity, string path, string message = "x")
    {
        return new SaleReadinessIssueDto
        {
            Severity = severity,
            Path = path,
            Message = message,
            Code = "Test:Issue",
        };
    }
}
