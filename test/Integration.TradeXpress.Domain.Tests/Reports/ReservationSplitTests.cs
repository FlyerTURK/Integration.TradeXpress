using System;
using Integration.TradeXpress.Reports;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// <b>REZERVASYON AYRIŞTIRMASI</b> — stok raporunun en kritik aritmetiği.
///
/// <para>Rezervasyon fiziksel <c>Net</c>'e karışırsa iki hata birden doğar: elimizde olmayan mal VARMIŞ
/// görünür (aşırı satış) ve yürüyen bakiye şişer. Ayrım tek satırlık bir filtreyle bozulabildiği için
/// doğrudan burada kilitlenir — rapor servisleri üzerinden dolaylı sınamak, kırılma sebebini gizlerdi.</para>
/// </summary>
public class ReservationSplitTests
{
    /// <summary>ASIL KURAL: rezervasyon Net'e GİRMEZ, ayrı sayaçta toplanır ve kullanılabilirden DÜŞER.</summary>
    [Fact]
    public void Reservation_stays_out_of_net_and_reduces_available()
    {
        var totals = ReservationSplit.Compute(new[]
        {
            new ReservationLeg(IsReservation: false, Amount: 50m, Quantity: 5m),    // fiziksel giriş
            new ReservationLeg(IsReservation: true, Amount: -20m, Quantity: -2m),   // müşteriye ayrıldı
        });

        totals.NetAmount.ShouldBe(50m);              // rezervasyon Net'i DEĞİŞTİRMEDİ
        totals.ReservedOutAmount.ShouldBe(20m);
        totals.AvailableAmount.ShouldBe(30m);        // 50 − 20

        totals.NetQuantity.ShouldBe(5m);
        totals.ReservedOutQuantity.ShouldBe(2m);
        totals.AvailableQuantity.ShouldBe(3m);
    }

    /// <summary>ReservedIn ("tedarikçiden beklenen") kullanılabilire EKLENMEZ — elimizde olmayan malı
    /// satılabilir göstermek, aşırı satışın en doğrudan yoludur.</summary>
    [Fact]
    public void Inbound_reservation_is_informational_and_never_increases_available()
    {
        var totals = ReservationSplit.Compute(new[]
        {
            new ReservationLeg(IsReservation: false, Amount: 10m, Quantity: 1m),
            new ReservationLeg(IsReservation: true, Amount: 100m, Quantity: 10m),   // tedarikçiden bekleniyor
        });

        totals.ReservedInAmount.ShouldBe(100m);
        totals.AvailableAmount.ShouldBe(10m);        // 100 EKLENMEDİ
        totals.AvailableQuantity.ShouldBe(1m);
    }

    /// <summary>Fiziksel giriş/çıkış ayrımı korunur — In/Out mutlak, Net işaretli.</summary>
    [Fact]
    public void Physical_in_and_out_are_absolute_while_net_is_signed()
    {
        var totals = ReservationSplit.Compute(new[]
        {
            new ReservationLeg(false, 100m, 10m),
            new ReservationLeg(false, -30m, -3m),
        });

        totals.InAmount.ShouldBe(100m);
        totals.OutAmount.ShouldBe(30m);
        totals.NetAmount.ShouldBe(70m);
        totals.InQuantity.ShouldBe(10m);
        totals.OutQuantity.ShouldBe(3m);
        totals.NetQuantity.ShouldBe(7m);
    }

    /// <summary>Stok tükenip fazlası rezerve edildiyse kullanılabilir EKSİYE düşer — ve düşmelidir
    /// (2026-08-05 Hakan kararı: *"hata yapmışsak cezasını biz çekeriz ki tutarlılık sürsün"*).
    /// Burada 0'a kırpmak defteri yalancı yapardı; kırpma KANAL sınırında yapılır, hesapta değil.</summary>
    [Fact]
    public void Available_can_go_negative_when_more_is_reserved_than_held()
    {
        var totals = ReservationSplit.Compute(new[]
        {
            new ReservationLeg(false, 10m, 1m),
            new ReservationLeg(true, -25m, -3m),
        });

        totals.AvailableAmount.ShouldBe(-15m);
        totals.AvailableQuantity.ShouldBe(-2m);
    }

    /// <summary>Leg'siz satır sıfırlarla döner (boş grup hesabı çökertmemeli).</summary>
    [Fact]
    public void No_legs_yields_zeroes()
    {
        var totals = ReservationSplit.Compute(Array.Empty<ReservationLeg>());

        totals.NetAmount.ShouldBe(0m);
        totals.AvailableAmount.ShouldBe(0m);
        totals.ReservedOutQuantity.ShouldBe(0m);
    }
}
