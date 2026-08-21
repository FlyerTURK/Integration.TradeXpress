using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// "KARAR BEKLEYENLER" LİSTESİ (<see cref="IOrderAppService.GetPendingDecisionsAsync"/> — 2026-08-21 Hakan
/// yerleşim kararı). Üç kaynak TEK uçta: ① Blocked rezervasyon (gerekçesiyle) ② iptal talebi bekleyen sipariş
/// ③ yaş eşiğini aşan aktif rezerv.
///
/// <para><b>Neden bu testler:</b> sekme SALT görünürlük katmanıdır ama görünürlüğün kendisi garantidir —
/// "kurulamayan rezervasyon sessiz atlanmaz" ve "hiçbir iptal otomatik değildir" sözleri ancak bekleyen iş
/// KENDİLİĞİNDEN görünüyorsa tutar. Liste yanlış şirketin işini gösterirse güvenlik sınırı, bekleyeni
/// göstermezse fail-closed vaadi delinir.</para>
/// </summary>
public abstract class OrderPendingDecisionListTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IOrderAppService _appService;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<OrderReservation, Guid> _reservationRepository;
    private readonly ICurrentCompany _currentCompany;

    protected OrderPendingDecisionListTests()
    {
        _appService = GetRequiredService<IOrderAppService>();
        _orderRepository = GetRequiredService<IRepository<Order, Guid>>();
        _reservationRepository = GetRequiredService<IRepository<OrderReservation, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    /// <summary>① Üç kaynak da listeye düşer, taze rezerv DÜŞMEZ; sıralama en uzun bekleyen üstte.
    /// <para>Yaş çıpaları tipine göre: iptal talebinin İLK anı · Blocked kaydının açıldığı an · rezervin
    /// kurulduğu an — üçü karışsaydı "ne zamandır bekliyor?" sorusu yanlış cevaplanır ve önceliklendirme
    /// (sekmenin tek amacı) çökerdi.</para></summary>
    [Fact]
    public async Task Blocked_cancellation_pending_and_aged_reservations_are_listed_oldest_first()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            // Sabit UTC anlar (saniye hassasiyeti) — provider round-trip'inde tik kaybı iddiaları bozmasın.
            var agedReservedAt = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
            var cancelRequestedAt = new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc);

            var blockedOrder = await SeedOrderAsync(companyId, "R-BLOCKED");
            await SeedReservationAsync(companyId, blockedOrder.Id,
                r => r.MarkBlocked("Kalem yerel varyanta eşleşmedi."));

            var cancelOrder = await SeedOrderAsync(companyId, "R-CANCEL");
            await SeedReservationAsync(companyId, cancelOrder.Id, r =>
            {
                r.MarkReserved(Guid.NewGuid(), new DateTime(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc));
                r.RequestCancellation(cancelRequestedAt);
            });

            var agedOrder = await SeedOrderAsync(companyId, "R-AGED");
            await SeedReservationAsync(companyId, agedOrder.Id,
                r => r.MarkReserved(Guid.NewGuid(), agedReservedAt));

            // Taze rezerv — eşik altı, karar bekleyen bir şey yok → listeye GİRMEZ.
            var freshOrder = await SeedOrderAsync(companyId, "R-FRESH");
            await SeedReservationAsync(companyId, freshOrder.Id,
                r => r.MarkReserved(Guid.NewGuid(), DateTime.UtcNow));

            var list = await _appService.GetPendingDecisionsAsync();

            list.Count.ShouldBe(3);
            list.ShouldAllBe(x => x.OrderId != freshOrder.Id);

            var blockedRow = list.Single(x => x.OrderId == blockedOrder.Id);
            blockedRow.Kind.ShouldBe(OrderPendingDecisionKind.BlockedReservation);
            blockedRow.Reason.ShouldBe("Kalem yerel varyanta eşleşmedi.");   // gerekçe satırda taşınır
            blockedRow.OrderNumber.ShouldBe("TY-R-BLOCKED");                 // sipariş bağlamı enrich edildi

            var cancelRow = list.Single(x => x.OrderId == cancelOrder.Id);
            cancelRow.Kind.ShouldBe(OrderPendingDecisionKind.CancellationRequested);
            cancelRow.PendingSinceUtc.ShouldBe(cancelRequestedAt);           // İLK talep anı — yaş gerçek bekleme

            var agedRow = list.Single(x => x.OrderId == agedOrder.Id);
            agedRow.Kind.ShouldBe(OrderPendingDecisionKind.AgingReservation);
            agedRow.PendingSinceUtc.ShouldBe(agedReservedAt);
            agedRow.ReservationStatus.ShouldBe(OrderReservationStatus.Reserved);   // ham eksen korunur

            // En uzun süredir bekleyen ÜSTTE: aged (08-01) → cancel (08-05) → blocked (şimdi).
            list[0].OrderId.ShouldBe(agedOrder.Id);
            list[1].OrderId.ShouldBe(cancelOrder.Id);
            list[2].OrderId.ShouldBe(blockedOrder.Id);
        }
    }

    /// <summary>② Başka şirketin bekleyen işi GÖRÜNMEZ — liste güvenlik sınırının (company-owned) içindedir.</summary>
    [Fact]
    public async Task Another_companys_pending_work_is_not_listed()
    {
        var foreignCompanyId = Guid.NewGuid();
        using (_currentCompany.Change(foreignCompanyId))
        {
            var foreignOrder = await SeedOrderAsync(foreignCompanyId, "R-FOREIGN");
            await SeedReservationAsync(foreignCompanyId, foreignOrder.Id,
                r => r.MarkBlocked("Yabancı şirketin bekleyen işi."));
        }

        var myCompanyId = Guid.NewGuid();
        using (_currentCompany.Change(myCompanyId))
        {
            var list = await _appService.GetPendingDecisionsAsync();
            list.ShouldBeEmpty();
        }
    }

    /// <summary>③ Tip önceliği: iptal talebi KURULAMADI'yı ezer (rozeti kullanıcıdan iş isteyen en acil durum
    /// belirler) — ama ham stok ekseni DTO'da korunur, bilgi gizlenmez.</summary>
    [Fact]
    public async Task Cancellation_request_outranks_blocked_in_the_kind_badge()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var order = await SeedOrderAsync(companyId, "R-BOTH");
            await SeedReservationAsync(companyId, order.Id, r =>
            {
                r.MarkBlocked("Reçete çözülemedi.");
                r.RequestCancellation(new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc));
            });

            var list = await _appService.GetPendingDecisionsAsync();

            var row = list.ShouldHaveSingleItem();   // aynı kayıt iki satır üretmez
            row.Kind.ShouldBe(OrderPendingDecisionKind.CancellationRequested);
            row.ReservationStatus.ShouldBe(OrderReservationStatus.Blocked);
            row.CancellationDecision.ShouldBe(OrderCancellationDecision.Pending);
        }
    }

    /// <summary>④ İptali ONAYLANMIŞ Blocked kayıt listeden düşer: sipariş kapandı, bağlanacak eşleşme kalmadı —
    /// süresiz listede kalması gürültü olurdu (kapanmayan iş, sekmenin sinyal değerini öldürür).</summary>
    [Fact]
    public async Task A_blocked_reservation_whose_cancellation_was_approved_is_no_longer_listed()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var order = await SeedOrderAsync(companyId, "R-CLOSED");
            await SeedReservationAsync(companyId, order.Id, r =>
            {
                r.MarkBlocked("Kalem eşleşmedi.");
                r.RequestCancellation(new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc));
                r.ApproveCancellation(decidedBy: null, new DateTime(2026, 8, 11, 9, 0, 0, DateTimeKind.Utc));
            });

            var list = await _appService.GetPendingDecisionsAsync();

            list.ShouldBeEmpty();
        }
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Yalın sipariş — kanal grafı KURULMADAN (id-only SalesChannelId yeter; sınanan şey listeleme,
    /// çekim zinciri değil). Kanal kaydı olmadığından SalesChannelCode null kalır — bu da meşru bir durumdur.</summary>
    private Task<Order> SeedOrderAsync(Guid companyId, string remoteOrderId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var order = new Order(companyId, Guid.NewGuid(), SalesChannelType.TrTrendyol, remoteOrderId, "TY-" + remoteOrderId);
            order.ApplyRemote("TY-" + remoteOrderId, DateTime.UtcNow.AddDays(-1), OrderStatus.New, "Created",
                "Müşteri", 100m, null, null, null, DateTime.UtcNow);
            return await _orderRepository.InsertAsync(order, autoSave: true);
        });
    }

    /// <summary>Doğrudan rezervasyon kaydı (OrderReservationCancellationBridgeTests deseni) — kurulum zinciri
    /// değil, listeleme sınanıyor.</summary>
    private Task SeedReservationAsync(Guid companyId, Guid orderId, Action<OrderReservation> arrange)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var reservation = new OrderReservation(companyId, orderId);
            arrange(reservation);
            await _reservationRepository.InsertAsync(reservation, autoSave: true);
        });
    }
}
