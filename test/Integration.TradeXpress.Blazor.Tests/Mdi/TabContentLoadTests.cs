using Integration.Framework.Blazor.Client.Services.Mdi;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Blazor.Tests.Mdi;

/// <summary>
/// MDI sekmesi yükleniyor-bileti sözleşmesi. İki kırılgan nokta buraya çivilenir:
/// <b>sayaç</b> (aynı sekmede birden çok yükleyici — panel erken kapanmamalı) ve
/// <b>idempotent Dispose</b> (bileşen hem normal akışta hem Dispose'da kapattığı için
/// çift kapatma NORMAL bir durum, sayacı bozmamalı).
/// </summary>
public class TabContentLoadTests
{
    [Fact]
    public void Starts_not_loading()
    {
        var load = new TabContentLoad();

        load.IsLoading.ShouldBeFalse();
    }

    [Fact]
    public void Begin_starts_loading()
    {
        var load = new TabContentLoad();

        load.Begin();

        load.IsLoading.ShouldBeTrue();
    }

    [Fact]
    public void Stays_loading_until_every_ticket_is_closed()
    {
        // SplitCrudView'de aynı sekmede İKİ CrudLayout yaşayabiliyor; tek bool olsaydı
        // ilk tamamlanan paneli erken kapatır, kullanıcı yarı dolu ekran görürdü.
        var load = new TabContentLoad();
        var first = load.Begin();
        var second = load.Begin();

        first.Dispose();

        load.IsLoading.ShouldBeTrue();

        second.Dispose();

        load.IsLoading.ShouldBeFalse();
    }

    [Fact]
    public void Disposing_the_same_ticket_twice_does_not_corrupt_the_counter()
    {
        var load = new TabContentLoad();
        var ticket = load.Begin();
        var other = load.Begin();

        ticket.Dispose();
        ticket.Dispose();   // ikinci kapatma sayacı DÜŞÜRMEMELİ

        load.IsLoading.ShouldBeTrue();

        other.Dispose();

        load.IsLoading.ShouldBeFalse();
    }

    [Fact]
    public void Raises_changed_only_on_real_transitions()
    {
        // Aradaki bilet alıp bırakmalar gereksiz render doğurmasın: yalnız 0↔1 geçişleri bildirilir.
        var load = new TabContentLoad();
        var raised = 0;
        load.Changed += () => raised++;

        var first = load.Begin();       // false → true : 1
        var second = load.Begin();      // zaten true  : bildirim yok
        first.Dispose();                // hâlâ true   : bildirim yok

        raised.ShouldBe(1);

        second.Dispose();               // true → false : 2

        raised.ShouldBe(2);
    }

    [Fact]
    public void Reports_loading_again_after_a_completed_cycle()
    {
        // Sağ-tık "Yenile" sekmeyi yeniden mount ediyor → panel tekrar görünmeli.
        var load = new TabContentLoad();
        load.Begin().Dispose();

        load.Begin();

        load.IsLoading.ShouldBeTrue();
    }
}
