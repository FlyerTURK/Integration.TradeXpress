using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.Permissions;
using Microsoft.Extensions.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Inbox.Providers;

/// <summary>
/// Ortak gelen kutusunun REZERVASYON kartı — kullanıcıdan İŞ BEKLEYEN iki durumu tek yerde toplar.
///
/// <para><b>Neden bu kart:</b> rezervasyon zincirinin iki garantisi vardı ama ikisinin de görünen yüzü yoktu:
/// <i>"kurulamayan rezervasyon SESSİZ atlanmaz"</i> (<c>Blocked</c>) ve <i>"hiçbir iptal otomatik değildir"</i>
/// (<c>Pending</c> karar). Kayıtlar yazılıyordu ama kimse onları aramıyordu — kullanıcı ancak bir sipariş
/// formunu tek tek açarsa görebilirdi. Bekleyen iş, arandığında değil KENDİLİĞİNDEN görünmelidir.</para>
///
/// <para><b>İKİ EKSEN TEK KARTTA:</b> stok ekseni (<c>Blocked</c> = sistem kuramadı) ve karar ekseni
/// (<c>Pending</c> = kullanıcı karar vermeli) farklı sebeplerdir ama ikisi de aynı soruyu sorar: "burada
/// benim yapmam gereken bir şey var mı?". Ayrı iki kart panoyu bölerdi.</para>
///
/// <para><b>Fail-closed:</b> izin yoksa ya da çalışılan şirket yoksa kart HİÇ gösterilmez (null). Şirketsiz
/// bağlamda sayı göstermek, başka şirketin bekleyen işini saymak olurdu.</para>
/// </summary>
[ExposeServices(typeof(IInboxSummaryProvider))]
public class OrderReservationInboxSummaryProvider : IInboxSummaryProvider, ITransientDependency
{
    /// <summary>Sipariş listesinin GERÇEK rotası.</summary>
    private const string OrdersRoute = "/orders";

    /// <summary><c>TradeXpressIcons.Order</c> değeri — sabit Blazor.Client'ta yaşar ve Application katmanı UI'ya
    /// referans VEREMEZ (katman yönü UI→Application), bu yüzden değer burada tekrarlanır.</summary>
    private const string OrderIconCssClass = "custom-icon-order";

    /// <summary>"Dikkat bekleyen" ölçütü — TEK KAYNAK: hem SQL sayımı hem satır bayrağı bunu kullanır, iki
    /// kopya sapamaz.</summary>
    private static readonly Expression<Func<OrderReservation, bool>> PendingExpression =
        reservation => reservation.Status == OrderReservationStatus.Blocked
                       || reservation.CancellationDecision == OrderCancellationDecision.Pending;

    private static readonly Func<OrderReservation, bool> IsPending = PendingExpression.Compile();

    private readonly IRepository<OrderReservation, Guid> _reservationRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IStringLocalizer<TradeXpressResource> _l;

    public OrderReservationInboxSummaryProvider(
        IRepository<OrderReservation, Guid> reservationRepository,
        IRepository<Order, Guid> orderRepository,
        ICurrentCompany currentCompany,
        IPermissionChecker permissionChecker,
        IAsyncQueryableExecuter asyncExecuter,
        IStringLocalizer<TradeXpressResource> l)
    {
        _reservationRepository = reservationRepository;
        _orderRepository = orderRepository;
        _currentCompany = currentCompany;
        _permissionChecker = permissionChecker;
        _asyncExecuter = asyncExecuter;
        _l = l;
    }

    public string SourceKey
    {
        get { return InboxSourceKey.OrderReservations; }
    }

    /// <summary>Teyitlerden SONRA, sorulardan ÖNCE: rezervasyon işi stok taahhüdüne dokunur, müşteri sorusundan
    /// daha aciledir.</summary>
    public int Order
    {
        get { return 20; }
    }

    public async Task<InboxCardDto?> BuildCardAsync(int recentCount)
    {
        if (!await _permissionChecker.IsGrantedAsync(TradeXpressPermissions.SalesChannels.Default))
        {
            return null;
        }

        if (_currentCompany.Id is not { } companyId || companyId == Guid.Empty)
        {
            return null;   // fail-closed: şirketsiz bağlamda başka şirketin işi sayılmaz
        }

        var reservations = (await _reservationRepository.GetQueryableAsync())
            .Where(r => r.CompanyId == companyId);

        var pendingCount = await _asyncExecuter.CountAsync(reservations.Where(PendingExpression));
        var totalCount = await _asyncExecuter.CountAsync(reservations);

        // Son satırlar: BEKLEYENLER önce (kart bir sayaç vitrinidir; kapanmış kaydı üste almak yanıltır).
        var recent = await _asyncExecuter.ToListAsync(
            reservations
                .OrderByDescending(r => r.CancellationRequestedAt ?? r.ReservedAt ?? r.CreationTime)
                .Take(recentCount));

        var orderIds = recent.Select(r => r.OrderId).ToList();
        var orderNumbers = await _asyncExecuter.ToListAsync(
            (await _orderRepository.GetQueryableAsync())
                .Where(o => orderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.OrderNumber }));

        var numberById = orderNumbers.ToDictionary(x => x.Id, x => x.OrderNumber);

        return new InboxCardDto
        {
            SourceKey     = SourceKey,
            Title         = _l["Inbox:OrderReservations"],
            IconCssClass  = OrderIconCssClass,
            PendingCount  = pendingCount,
            TotalCount    = totalCount,
            TargetUrl     = OrdersRoute,
            RecentItems   = recent.Select(r => new InboxCardItemDto
            {
                Id            = r.OrderId,
                PrimaryText   = numberById.GetValueOrDefault(r.OrderId) ?? r.OrderId.ToString(),
                SecondaryText = DescribeState(r),
                OccurredAt    = r.CancellationRequestedAt ?? r.ReservedAt ?? r.CreationTime,
                IsPending     = IsPending(r),
            }).ToList(),
        };
    }

    /// <summary>Satırın NEDEN listede olduğunu söyler. Karar bekleyen durum stok durumundan ÖNCE gelir:
    /// kullanıcıdan iş isteyen asıl şey odur.</summary>
    private string DescribeState(OrderReservation reservation)
    {
        if (reservation.CancellationDecision == OrderCancellationDecision.Pending)
        {
            return _l["Enum:OrderCancellationDecision:Pending"];
        }

        if (reservation.Status == OrderReservationStatus.Blocked)
        {
            // Gerekçe varsa onu göster — "Kurulamadı" tek başına kullanıcıya ne yapacağını söylemez.
            return string.IsNullOrWhiteSpace(reservation.Note)
                ? _l["Enum:OrderReservationStatus:Blocked"]
                : reservation.Note;
        }

        return _l[$"Enum:OrderReservationStatus:{reservation.Status}"];
    }
}
