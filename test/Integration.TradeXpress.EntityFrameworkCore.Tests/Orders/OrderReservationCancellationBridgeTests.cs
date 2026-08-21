using System;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// KANAL İPTAL SİNYALİ → REZERVASYONUN KARAR EKSENİ.
///
/// <para><b>Kapatılan açık:</b> <c>OrderReservation.RequestCancellation</c>'ın üretim kodunda SIFIR çağıranı
/// vardı — yalnız tanım. Karar ekseni hiçbir zaman <c>Pending</c>'e düşmüyordu, dolayısıyla kullanıcı yüzündeki
/// Onayla/Reddet düğmeleri ölü veriye bağlanacaktı: kanal iptal ister, sistem hiç duymaz, rezervasyon sessizce
/// tutulmaya devam eder.</para>
///
/// <para><b>İKİ EKSEN AYRI KALIR</b> (§6): iptal sinyali STOK eksenine dokunmaz. Kanal iptal dedi diye madeni
/// kendiliğinden geri vermek, mal fiziksel olarak hazırlanmış/kesilmiş olabilecekken defteri yalanlamak olurdu.
/// Hiçbir iptal otomatik değildir.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class OrderReservationCancellationBridgeTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly OrderReservationManager _manager;
    private readonly IRepository<OrderReservation, Guid> _reservations;
    private readonly IClock _clock;

    public OrderReservationCancellationBridgeTests()
    {
        _manager      = GetRequiredService<OrderReservationManager>();
        _reservations = GetRequiredService<IRepository<OrderReservation, Guid>>();
        _clock        = GetRequiredService<IClock>();
    }

    /// <summary>① Sinyal karar eksenini uyandırır; STOK ekseni DEĞİŞMEZ.</summary>
    [Fact]
    public async Task Cancellation_signal_sets_pending_without_touching_the_stock_axis()
    {
        var orderId = await SeedReservationAsync(r => r.MarkReserved(
            SimpleGuidGenerator.Instance.Create(), DateTime.UtcNow));

        var updated = await WithUnitOfWorkAsync(() => _manager.NotifyCancellationRequestedAsync(orderId));

        updated!.CancellationDecision.ShouldBe(OrderCancellationDecision.Pending);
        updated.CancellationRequestedAt.ShouldNotBeNull();
        updated.Status.ShouldBe(OrderReservationStatus.Reserved);   // ⚠ stok ekseni DOKUNULMADI
    }

    /// <summary>② Verilmiş KARAR kanalın tekrar eden sinyaliyle ezilmez.
    /// <para>Worker 2 dakikada bir aynı siparişle döner; ezilseydi operatörün "reddettim" kararı her turda
    /// sessizce "karar bekliyor"a geri düşerdi ve iş asla kapanmazdı.</para></summary>
    [Fact]
    public async Task Repeated_signal_does_not_overwrite_a_decision_already_made()
    {
        var now = _clock.Now.ToUniversalTime();
        var orderId = await SeedReservationAsync(r =>
        {
            r.MarkReserved(SimpleGuidGenerator.Instance.Create(), now);
            r.RequestCancellation(now);
            r.RejectCancellation(decidedBy: null, now, "Mal hazırlandı.");
        });

        var updated = await WithUnitOfWorkAsync(() => _manager.NotifyCancellationRequestedAsync(orderId));

        updated!.CancellationDecision.ShouldBe(OrderCancellationDecision.Rejected);
    }

    /// <summary>③ Zaten BEKLİYORSA ilk talep anı korunur.
    /// <para>Timestamp her turda tazelenseydi "ne zamandır karar bekliyor?" sorusunun cevabı DAİMA "2 dakikadır"
    /// olurdu — bekleyen işi önceliklendirmek imkânsızlaşırdı.</para></summary>
    [Fact]
    public async Task Second_signal_keeps_the_original_request_timestamp()
    {
        var firstAt = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        var orderId = await SeedReservationAsync(r =>
        {
            r.MarkReserved(SimpleGuidGenerator.Instance.Create(), firstAt);
            r.RequestCancellation(firstAt);
        });

        var updated = await WithUnitOfWorkAsync(() => _manager.NotifyCancellationRequestedAsync(orderId));

        updated!.CancellationRequestedAt.ShouldBe(firstAt);
    }

    /// <summary>④ SERBEST BIRAKILMIŞ rezervasyon uyandırılmaz — geri verilecek stok kalmadı, karar da yok.</summary>
    [Fact]
    public async Task Released_reservation_is_not_reopened_by_a_cancellation_signal()
    {
        var now = _clock.Now.ToUniversalTime();
        var orderId = await SeedReservationAsync(r =>
        {
            r.MarkReserved(SimpleGuidGenerator.Instance.Create(), now);
            r.MarkReleased(now, "İptal onaylandı.");
        });

        var updated = await WithUnitOfWorkAsync(() => _manager.NotifyCancellationRequestedAsync(orderId));

        updated!.CancellationDecision.ShouldBe(OrderCancellationDecision.None);
        updated.Status.ShouldBe(OrderReservationStatus.Released);
    }

    /// <summary>⑤ İADE/DEĞİŞİM talebi (kod 52/53) <c>NotifyCancellationRequestedAsync</c>'i TETİKLEMEZ — onlar iptal değil, iade sürecidir.
    /// <para>Aynı yola bağlansaydı teslim edilmiş bir siparişin iade talebi "iptal kararı bekliyor" diye
    /// görünürdü; operatör onaylarsa stok geri verilir ama mal hâlâ müşteridedir.</para></summary>
    [Theory]
    [InlineData("51", true)]    // İptal Talep Edildi
    [InlineData("52", false)]   // İade Talep Edildi
    [InlineData("53", false)]   // Değişim Talep Edildi
    [InlineData("1", false)]
    [InlineData(null, false)]
    [InlineData("abc", false)]
    public void Only_code_51_is_a_cancellation_request(string? rawItemStatus, bool expected)
    {
        N11OrderStatusCatalog.IsCancellationRequested(rawItemStatus).ShouldBe(expected);
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Sipariş grafı KURULMADAN doğrudan rezervasyon kaydı — sınanan şey <c>NotifyCancellationRequestedAsync</c>'in karar mantığı,
    /// rezervasyon kurulumu değil (onun kendi testleri var).</summary>
    private Task<Guid> SeedReservationAsync(Action<OrderReservation> arrange)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var companyId = SimpleGuidGenerator.Instance.Create();
            var orderId = SimpleGuidGenerator.Instance.Create();

            var reservation = new OrderReservation(companyId, orderId);
            arrange(reservation);
            await _reservations.InsertAsync(reservation, autoSave: true);

            return orderId;
        });
    }
}
