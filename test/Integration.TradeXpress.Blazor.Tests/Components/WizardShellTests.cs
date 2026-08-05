using System.Collections.Generic;
using System.Linq;
using Bunit;
using Integration.Framework.Blazor.Client.Components.Shared;
using Microsoft.AspNetCore.Components;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Components;

/// <summary>
/// <see cref="WizardShell"/> — yeniden kullanılabilir Framework bileşeni olduğu için unit testi ZORUNLU
/// (CLAUDE.md §4). Testler kabuğun DAVRANIŞ sözleşmesini kilitler, görünümünü değil.
///
/// <para>Kilitlenen dört değişmez: (1) yalnız AKTİF adımın gövdesi çizilir; (2) "İleri" adımın işini koşar ve
/// iş <c>Cancel()</c> derse adım DEĞİŞMEZ; (3) "Geri" doğrulama KOŞMAZ; (4) şeritten İLERİ atlanamaz.</para>
/// </summary>
public class WizardShellTests : BlazorComponentTestBase
{
    /// <summary>Pasif adımın gövdesi hiç render EDİLMEMELİ — görünmeyen bir alan doğrulamayı tetikleyebilir ve
    /// ağ çağrısı yapan bir alt bileşen sırası gelmeden koşabilir.</summary>
    [Fact]
    public void Only_the_active_step_renders_its_body()
    {
        var cut = RenderTwoStepWizard();

        cut.Markup.ShouldContain("BIRINCI-ADIM-GOVDESI");
        cut.Markup.ShouldNotContain("IKINCI-ADIM-GOVDESI");
    }

    [Fact]
    public void Next_advances_to_the_following_step()
    {
        var cut = RenderTwoStepWizard();

        ClickButton(cut, "Wizard:Next");

        cut.Markup.ShouldContain("IKINCI-ADIM-GOVDESI");
        cut.Markup.ShouldNotContain("BIRINCI-ADIM-GOVDESI");
    }

    /// <summary>Adımın işi <c>Cancel()</c> derse adım DEĞİŞMEZ. Bu geçmezse yarım kurulumla ilerlenir ve hata
    /// bir sonraki adımda, sebebinden kopuk biçimde ortaya çıkar.</summary>
    [Fact]
    public void Cancelled_step_work_blocks_the_advance()
    {
        var ran = 0;
        var cut = Render<WizardShell>(parameters => parameters
            .AddChildContent<WizardStep>(step => step
                .Add(s => s.Title, "Bir")
                .Add(s => s.OnBeforeAdvanceAsync, EventCallback.Factory.Create<WizardStepAdvanceContext>(
                    this, context => { ran++; context.Cancel(); }))
                .AddChildContent("<span>BIRINCI-ADIM-GOVDESI</span>"))
            .AddChildContent<WizardStep>(step => step
                .Add(s => s.Title, "Iki")
                .AddChildContent("<span>IKINCI-ADIM-GOVDESI</span>")));

        ClickButton(cut, "Wizard:Next");

        ran.ShouldBe(1);                                        // iş KOŞTU
        cut.Markup.ShouldContain("BIRINCI-ADIM-GOVDESI");       // ama adım DEĞİŞMEDİ
        cut.Markup.ShouldNotContain("IKINCI-ADIM-GOVDESI");
    }

    /// <summary>"Geri" adımın işini KOŞMAZ — kullanıcı yazdığını gözden geçirmek için serbestçe geri
    /// dönebilmeli; geri giderken doğrulamak, düzeltmek isteyeni düzeltmeden engellemek olurdu.</summary>
    [Fact]
    public void Back_does_not_run_the_step_work()
    {
        var ran = 0;
        var cut = Render<WizardShell>(parameters => parameters
            .AddChildContent<WizardStep>(step => step
                .Add(s => s.Title, "Bir")
                .AddChildContent("<span>BIRINCI-ADIM-GOVDESI</span>"))
            .AddChildContent<WizardStep>(step => step
                .Add(s => s.Title, "Iki")
                .Add(s => s.OnBeforeAdvanceAsync, EventCallback.Factory.Create<WizardStepAdvanceContext>(
                    this, _ => { ran++; }))
                .AddChildContent("<span>IKINCI-ADIM-GOVDESI</span>")));

        ClickButton(cut, "Wizard:Next");   // 2. adıma geç (1. adımın işi yok)
        ran.ShouldBe(0);

        ClickButton(cut, "Wizard:Back");   // geri dön — 2. adımın İŞİ KOŞMAMALI

        ran.ShouldBe(0);
        cut.Markup.ShouldContain("BIRINCI-ADIM-GOVDESI");
    }

    /// <summary>Son adımda "İleri" yerine "Bitir" çıkar ve <c>OnFinished</c> tetiklenir.</summary>
    [Fact]
    public void Finish_fires_only_on_the_last_step()
    {
        var finished = 0;
        var cut = Render<WizardShell>(parameters => parameters
            .Add(p => p.OnFinished, EventCallback.Factory.Create(this, () => { finished++; }))
            .AddChildContent<WizardStep>(step => step
                .Add(s => s.Title, "Bir")
                .AddChildContent("<span>BIRINCI-ADIM-GOVDESI</span>"))
            .AddChildContent<WizardStep>(step => step
                .Add(s => s.Title, "Iki")
                .AddChildContent("<span>IKINCI-ADIM-GOVDESI</span>")));

        // İlk adımda Bitir düğmesi YOK.
        FindButton(cut, "Wizard:Finish").ShouldBeNull();

        ClickButton(cut, "Wizard:Next");
        ClickButton(cut, "Wizard:Finish");

        finished.ShouldBe(1);
    }

    /// <summary>"Atla" işaretli adımda adımın işi KOŞULMADAN ilerlenir; işaretsiz adımda düğme hiç çıkmaz.</summary>
    [Fact]
    public void Skip_advances_without_running_the_step_work()
    {
        var ran = 0;
        var cut = Render<WizardShell>(parameters => parameters
            .AddChildContent<WizardStep>(step => step
                .Add(s => s.Title, "Bir")
                .Add(s => s.CanSkip, true)
                .Add(s => s.OnBeforeAdvanceAsync, EventCallback.Factory.Create<WizardStepAdvanceContext>(
                    this, _ => { ran++; }))
                .AddChildContent("<span>BIRINCI-ADIM-GOVDESI</span>"))
            .AddChildContent<WizardStep>(step => step
                .Add(s => s.Title, "Iki")
                .AddChildContent("<span>IKINCI-ADIM-GOVDESI</span>")));

        ClickButton(cut, "Wizard:Skip");

        ran.ShouldBe(0);
        cut.Markup.ShouldContain("IKINCI-ADIM-GOVDESI");
    }

    [Fact]
    public void Skip_button_is_absent_on_a_step_that_cannot_be_skipped()
    {
        var cut = RenderTwoStepWizard();

        FindButton(cut, "Wizard:Skip").ShouldBeNull();
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────────────────────────────

    private IRenderedComponent<WizardShell> RenderTwoStepWizard()
    {
        return Render<WizardShell>(parameters => parameters
            .AddChildContent<WizardStep>(step => step
                .Add(s => s.Title, "Bir")
                .AddChildContent("<span>BIRINCI-ADIM-GOVDESI</span>"))
            .AddChildContent<WizardStep>(step => step
                .Add(s => s.Title, "Iki")
                .AddChildContent("<span>IKINCI-ADIM-GOVDESI</span>")));
    }

    /// <summary>Düğmeyi METNİNDEN bulur. Lokalizasyon sahtesi anahtarı aynen döndürdüğü için metin = anahtar
    /// ("Wizard:Next"); böylece test çeviri metnine değil SÖZLEŞMEYE bağlanır.</summary>
    private static AngleSharp.Dom.IElement? FindButton(IRenderedComponent<WizardShell> cut, string key)
    {
        return cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains(key));
    }

    private static void ClickButton(IRenderedComponent<WizardShell> cut, string key)
    {
        var button = FindButton(cut, key);
        button.ShouldNotBeNull($"'{key}' düğmesi bulunamadı. Bulunanlar: {string.Join(" | ", cut.FindAll("button").Select(b => b.TextContent.Trim()))}");
        button!.Click();
    }
}
