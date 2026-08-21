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
/// İŞARETLİ BAŞLIĞIN GÖRÜNÜR SÖZLEŞMESİ (2026-08-19) — sekme başlıkları, varyant reçete bölümü ve kanal ürünü
/// formları hep bu bileşenden geçtiği için renklendirme kuralının tek sınandığı yer burasıdır.
///
/// <para>Kilitlenenler: (1) endeks YOKKA başlık nötr kalır — satışa hazırlık paneli olmayan formlarda da kullanılıyor;
/// (2) Info issue'su başlığı BOYAMAZ ve rozet açmaz (KDV issue'su dersi); (3) karar gerektiren issue başlığı
/// ağırlığın rengiyle boyar ve KARAR GEREKTİREN issue'ları sayar; (4) kapsam dışındaki issue başlığa
/// yansımaz; (5) BOŞ KAPSAM iki farklı şey demek olabilir — satışa hazırlık paneli başlığında "tüm ürün" (varsayılan), kanal
/// ekranında "kayıt henüz yok" (<c>TreatNullScopeAsRoot=false</c>); ikisi karışırsa yeni açılan kanal sekmesi
/// ürünün tüm issue'larıyla boyanır.</para>
/// </summary>
public class SaleReadinessCaptionTests : BlazorComponentTestBase
{
    private static readonly Guid VariantId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Without_an_index_the_caption_stays_neutral()
    {
        var cut = Render<SaleReadinessCaption>(parameters => parameters
            .Add(c => c.Text, "Varyantlar")
            .Add(c => c.Scope, SaleReadinessScope.Variants));

        cut.Markup.ShouldContain("Varyantlar");
        cut.Markup.ShouldNotContain("--dxbl-danger");
        cut.Markup.ShouldNotContain("--dxbl-warning");
    }

    [Fact]
    public void An_info_finding_neither_colours_nor_badges_the_caption()
    {
        var index = Build(Issue(SaleReadinessSeverity.Info, SaleReadinessScope.Variants));

        var cut = Render<SaleReadinessCaption>(parameters => parameters
            .Add(c => c.Text, "Varyantlar")
            .Add(c => c.Scope, SaleReadinessScope.Variants)
            .Add(c => c.Index, index));

        cut.Markup.ShouldNotContain("--dxbl-danger");
        cut.Markup.ShouldNotContain("--dxbl-info");
    }

    [Fact]
    public void A_blocker_colours_the_caption_and_counts_the_actionable_findings()
    {
        var index = Build(
            Issue(SaleReadinessSeverity.Error, SaleReadinessScope.VariantRecipe(VariantId), "temel emtia yok"),
            Issue(SaleReadinessSeverity.Warning, SaleReadinessScope.Variant(VariantId)),
            Issue(SaleReadinessSeverity.Info, SaleReadinessScope.Variants));

        var cut = Render<SaleReadinessCaption>(parameters => parameters
            .Add(c => c.Text, "Varyantlar")
            .Add(c => c.Scope, SaleReadinessScope.Variants)
            .Add(c => c.Index, index));

        cut.Markup.ShouldContain("--dxbl-danger");
        // Rozet Info'yu SAYMAZ → 3 issue'nun 2'si.
        cut.Markup.ShouldContain(">2<");
        // İpucu, kapsamdaki ilk karar gerektiren issue'nun kendi cümlesidir.
        cut.Markup.ShouldContain("temel emtia yok");
    }

    [Fact]
    public void A_finding_outside_the_scope_does_not_reach_the_caption()
    {
        var index = Build(Issue(SaleReadinessSeverity.Error, SaleReadinessScope.Media));

        var cut = Render<SaleReadinessCaption>(parameters => parameters
            .Add(c => c.Text, "Varyantlar")
            .Add(c => c.Scope, SaleReadinessScope.Variants)
            .Add(c => c.Index, index));

        cut.Markup.ShouldNotContain("--dxbl-danger");
    }

    [Fact]
    public void The_cascaded_index_is_used_when_no_explicit_index_is_given()
    {
        var index = Build(Issue(SaleReadinessSeverity.Error, SaleReadinessScope.Channels));

        var cut = Render<SaleReadinessCaption>(parameters => parameters
            .AddCascadingValue("SaleReadinessIndex", index)
            .Add(c => c.Text, "Satış Kanalı Ürünleri")
            .Add(c => c.Scope, SaleReadinessScope.Channels));

        cut.Markup.ShouldContain("--dxbl-danger");
    }

    /// <summary>KÖK BAŞLIK (ürünün satışa hazırlık paneli) kapsamsız çizilir ve orada "kapsam yok" gerçekten "tüm ürün"
    /// demektir — varsayılan davranış budur.</summary>
    [Fact]
    public void Without_a_scope_the_caption_reports_the_whole_product_by_default()
    {
        var index = Build(
            Issue(SaleReadinessSeverity.Error, SaleReadinessScope.Media),
            Issue(SaleReadinessSeverity.Warning, SaleReadinessScope.Variants));

        var cut = Render<SaleReadinessCaption>(parameters => parameters
            .Add(c => c.Text, "Satışa Hazırlık")
            .Add(c => c.Index, index));

        cut.Markup.ShouldContain("--dxbl-danger");
        cut.Markup.ShouldContain(">2<");
    }

    /// <summary>KANAL EKRANI tuzağı: kanal ürünü henüz kaydedilmemişken kapsam yoktur ve bu "kök" sayılırsa yeni
    /// açılan sekme ürünün TÜM issue'larıyla kırmızıya boyanır — kullanıcı olmayan bir soruna yönlendirilir.
    /// Kanal ekranları bu yüzden <c>TreatNullScopeAsRoot="false"</c> geçer.</summary>
    [Fact]
    public void A_channel_surface_without_an_id_stays_neutral_although_the_product_has_findings()
    {
        var index = Build(
            Issue(SaleReadinessSeverity.Error, SaleReadinessScope.Media),
            Issue(SaleReadinessSeverity.Warning, SaleReadinessScope.Variants));

        var cut = Render<SaleReadinessCaption>(parameters => parameters
            .Add(c => c.Text, "Kombinasyonlar")
            .Add(c => c.Scope, null)
            .Add(c => c.TreatNullScopeAsRoot, false)
            .Add(c => c.Index, index));

        cut.Markup.ShouldContain("Kombinasyonlar");
        cut.Markup.ShouldNotContain("--dxbl-danger");
        cut.Markup.ShouldNotContain("--dxbl-warning");
        // Rozet de açılmaz: sayaç kökten okusaydı sekme "2" derdi.
        cut.Markup.ShouldNotContain(">2<");
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
