using System;
using System.Collections.Generic;
using Bunit;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Products;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Components;

/// <summary>
/// Satışa-hazırlık işaretinin (<see cref="ReadinessMark"/>) davranış sözleşmesi (2026-08-19).
///
/// <para>Kilitlenenler: (1) endeks yoksa ya da boşsa HİÇBİR ŞEY çizilmez — "bilinmiyor" ile "issue yok" aynı
/// sonucu verir, ekran gereksiz işaretle dolmaz; (2) kapsam boşsa (kaydedilmemiş satır) işaret yok — boş kapsamı
/// kök sayıp ürünün TÜM issue'larını o satıra yapıştırmak yanlış olurdu; (3) Error kapsamı danger rengiyle ve o
/// kapsamdaki issue'nun mesajıyla çizilir; (4) <b>Info çizmez</b> — KDV issue'su hiçbir başlığı renklendirmesin
/// diye konulan kural (Hakan, aynı gün) burada mekanik olarak tutulur.</para>
/// </summary>
public class ReadinessMarkTests : BlazorComponentTestBase
{
    private const string ChannelScope = "channels/11111111-1111-1111-1111-111111111111";

    [Fact]
    public void Draws_nothing_without_an_index()
    {
        // Endeks HİÇ bağlanmaz — ürün formunun cascade'i altında olmayan bileşenin durumu budur.
        var cut = Render<ReadinessMark>(parameters => parameters
            .Add(p => p.Scope, ChannelScope));

        cut.Markup.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void Draws_nothing_for_an_empty_index()
    {
        var cut = Render<ReadinessMark>(parameters => parameters
            .Add(p => p.Index, SaleReadinessIssueIndex.Empty)
            .Add(p => p.Scope, ChannelScope));

        cut.Markup.Trim().ShouldBeEmpty();
    }

    /// <summary>Kaydedilmemiş satırın kapsamı yoktur; boş kapsam "kök" sayılıp ürünün tüm issue'ları buraya
    /// düşmemeli.</summary>
    [Fact]
    public void Draws_nothing_without_a_scope_even_though_the_index_has_issues()
    {
        var cut = Render<ReadinessMark>(parameters => parameters
            .Add(p => p.Index, IndexWith(SaleReadinessSeverity.Error, ChannelScope, "reçetede emtia yok"))
            .Add(p => p.Scope, null));

        cut.Markup.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void An_error_scope_is_drawn_in_danger_color_with_the_issue_message()
    {
        var cut = Render<ReadinessMark>(parameters => parameters
            .Add(p => p.Index, IndexWith(SaleReadinessSeverity.Error, ChannelScope, "reçetede emtia yok"))
            .Add(p => p.Scope, ChannelScope));

        // Renk tema değişkeninden gelir (sabit hex serpilmez) — palet sözleşmesi.
        SaleReadinessPalette.Danger.ShouldContain("--dxbl-danger");
        cut.Markup.ShouldContain("--dxbl-danger");
        cut.Markup.ShouldContain("reçetede emtia yok");
    }

    /// <summary>Issue ALT kapsamda olsa da üst ekran işaretlenir — "bulgu bulunduğu HER seviyede görünür".</summary>
    [Fact]
    public void A_nested_issue_marks_the_enclosing_scope()
    {
        var deepPath = ChannelScope + "/variants/22222222-2222-2222-2222-222222222222/recipe";

        var cut = Render<ReadinessMark>(parameters => parameters
            .Add(p => p.Index, IndexWith(SaleReadinessSeverity.Warning, deepPath, "varyantın reçetesi yok"))
            .Add(p => p.Scope, ChannelScope));

        cut.Markup.ShouldContain("--dxbl-warning");
    }

    /// <summary>Info hiçbir ekranı renklendirmez — her bilgi satırı başlık boyarsa renk anlamını yitirir.</summary>
    [Fact]
    public void An_info_scope_is_not_drawn()
    {
        var cut = Render<ReadinessMark>(parameters => parameters
            .Add(p => p.Index, IndexWith(SaleReadinessSeverity.Info, ChannelScope, "KDV oranı girilmemiş"))
            .Add(p => p.Scope, ChannelScope));

        cut.Markup.Trim().ShouldBeEmpty();
    }

    /// <summary>Rozet kipi karar gerektiren issue'ları SAYAR; aynı kapsamdaki Info satırı sayacı şişirmez.</summary>
    [Fact]
    public void The_heading_badge_counts_actionable_issues_only()
    {
        var index = new SaleReadinessIssueIndex(new List<SaleReadinessIssueDto>
        {
            Issue(SaleReadinessSeverity.Error, ChannelScope, "reçetede emtia yok"),
            Issue(SaleReadinessSeverity.Warning, ChannelScope, "görsel yok"),
            Issue(SaleReadinessSeverity.Info, ChannelScope, "KDV oranı girilmemiş"),
        });

        var cut = Render<ReadinessMark>(parameters => parameters
            .Add(p => p.Index, index)
            .Add(p => p.Scope, ChannelScope)
            .Add(p => p.Mode, ReadinessMarkMode.HeadingBadge));

        cut.Markup.ShouldContain(">2<");
        cut.Markup.ShouldContain("--dxbl-danger");
    }

    /// <summary>Kök işareti KASTEN istenebilir (ürünün satışa hazırlık panelinin kendi başlığı <c>SaleReadinessCaption</c>
    /// üzerinden bunu geçer). Bayrak açıkken boş kapsam "tüm ürün" demektir; varsayılan hâlâ kapalıdır ki bir
    /// satır işareti kazara kökü göstermesin.</summary>
    [Fact]
    public void An_empty_scope_reports_the_root_only_when_it_is_explicitly_asked_for()
    {
        var cut = Render<ReadinessMark>(parameters => parameters
            .Add(p => p.Index, IndexWith(SaleReadinessSeverity.Error, ChannelScope, "reçetede emtia yok"))
            .Add(p => p.Scope, null)
            .Add(p => p.TreatNullScopeAsRoot, true));

        cut.Markup.ShouldContain("--dxbl-danger");
        cut.Markup.ShouldContain("reçetede emtia yok");
    }

    private static SaleReadinessIssueIndex IndexWith(SaleReadinessSeverity severity, string path, string message)
    {
        return new SaleReadinessIssueIndex(new List<SaleReadinessIssueDto> { Issue(severity, path, message) });
    }

    private static SaleReadinessIssueDto Issue(SaleReadinessSeverity severity, string path, string message)
    {
        return new SaleReadinessIssueDto
        {
            Severity = severity,
            Path = path,
            Message = message,
            Code = "Test:Issue",
            StepKey = "ChannelProducts",
            TargetId = Guid.NewGuid(),
        };
    }
}
