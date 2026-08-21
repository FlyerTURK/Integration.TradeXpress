using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Geography;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Trendyol;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// NÖTR sipariş uygulaması — ORTAK SİPARİŞ PANELİ (tüm kanallar tek grid, kanal yalnız discriminator) + pazaryerinden
/// SALT-OKUMA çekim (O0) + YEREL düzeltme katmanı (O1) + N11'e YAZAN state machine aksiyonları (O2 — kabul/red/kargo,
/// GERÇEK ve geri alınamaz). <b>Company-owned + per-tenant</b> (sunucu <see cref="ICurrentCompany"/> zorlar).
/// + REZERVASYON (O3 — 2026-08-06: eski doc "FİŞ YOK, REZERVASYON YOK, STOK HAREKETİ YOK" diyordu; artık sipariş
/// çekildiği anda reçetedeki emtia <c>PaymentType=Reservation</c> fişiyle müşteriye ayrılır — fiziksel Net'e girmez,
/// kullanılabilirden düşer. İptal HİÇBİR ZAMAN otomatik değildir; serbest bırakmayı kullanıcı kararı tetikler).
/// İdempotency anahtarı (SalesChannelId, RemoteOrderId): ikinci çekim
/// durumu/satırları günceller, dublike üretmez. Satırlar KENDİ tablosunda (id-only OrderId) — çekimde sil+yaz.
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class OrderAppService : TradeXpressAppService, IOrderAppService
{
    private const string ProductEntityName = "Product";

    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<OrderLine, Guid> _orderLineRepository;
    private readonly IRepository<OrderOperationalData, Guid> _operationalDataRepository;
    private readonly IRepository<OrderLineOperationalData, Guid> _operationalLineRepository;
    // Sku.ProductVariantId artık JENERİK EntityVariant.Id taşır (agnostik varyant geçişi) — eşleşme agnostik tabloya çözülür.
    private readonly IRepository<EntityVariant, Guid> _productVariantRepository;
    private readonly IRepository<Products.Product, Guid> _productRepository;   // yalnız OKUMA — eşleştirme adayları
    private readonly IRepository<SalesChannelBase, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrN11, Guid> _n11ChannelRepository;
    // Sipariş adres picker'ının TR ön-seçimi için host-global coğrafya (Country=TR id + il/ilçe isim-eşleşmesi; N11 YOK).
    private readonly IRepository<Country, Guid> _countryRepository;
    private readonly IRepository<AdministrativeArea, Guid> _administrativeAreaRepository;
    private readonly IRepository<Locality, Guid> _localityRepository;
    private readonly IDataFilter _dataFilter;
    private readonly OrderSyncManager _orderSyncManager;
    private readonly OrderLineProductSnapshotBuilder _snapshotBuilder;
    private readonly IN11OrderClient _n11OrderClient;
    private readonly ICurrentCompany _currentCompany;
    private readonly IRepository<OrderReservation, Guid> _reservationRepository;
    private readonly IRepository<OrderFulfillmentLink, Guid> _fulfillmentLinkRepository;
    private readonly OrderReservationManager _reservationManager;
    private readonly IRepository<Vouchers.Voucher, Guid> _voucherRepository;
    private readonly Vouchers.IVoucherAppService _voucherAppService;

    // Ortak panel liste sorgusunda filtre/sıralama/aramaya İZİN VERİLEN alanlar (whitelist — Order entity property
    // adları). CompanyId sunucu-zorlamalı olduğundan whitelist'te YOK (client daraltamaz). Id tie-breaker için dahil.
    private static readonly HashSet<string> OrderListAllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Order.Id),
        nameof(Order.SalesChannelId),
        nameof(Order.ChannelType),
        nameof(Order.OrderNumber),
        nameof(Order.OrderDate),
        nameof(Order.NeutralStatus),
        nameof(Order.RemoteStatus),
        nameof(Order.CustomerName),
        nameof(Order.TotalAmount),
        nameof(Order.CurrencyUnitId),
        nameof(Order.CargoProvider),
        nameof(Order.CargoTrackingNumber),
        nameof(Order.FetchedAt),
    };

    public OrderAppService(
        IRepository<Order, Guid> orderRepository,
        IRepository<OrderLine, Guid> orderLineRepository,
        IRepository<OrderOperationalData, Guid> operationalDataRepository,
        IRepository<OrderLineOperationalData, Guid> operationalLineRepository,
        IRepository<EntityVariant, Guid> productVariantRepository,
        IRepository<Products.Product, Guid> productRepository,
        IRepository<SalesChannelBase, Guid> channelRepository,
        IRepository<SalesChannelTrN11, Guid> n11ChannelRepository,
        IRepository<Country, Guid> countryRepository,
        IRepository<AdministrativeArea, Guid> administrativeAreaRepository,
        IRepository<Locality, Guid> localityRepository,
        IDataFilter dataFilter,
        OrderSyncManager orderSyncManager,
        OrderLineProductSnapshotBuilder snapshotBuilder,
        IN11OrderClient n11OrderClient,
        ICurrentCompany currentCompany,
        IRepository<OrderReservation, Guid> reservationRepository,
        IRepository<OrderFulfillmentLink, Guid> fulfillmentLinkRepository,
        OrderReservationManager reservationManager,
        IRepository<Vouchers.Voucher, Guid> voucherRepository,
        Vouchers.IVoucherAppService voucherAppService)
    {
        _orderRepository = orderRepository;
        _orderLineRepository = orderLineRepository;
        _operationalDataRepository = operationalDataRepository;
        _operationalLineRepository = operationalLineRepository;
        _productVariantRepository = productVariantRepository;
        _productRepository = productRepository;
        _channelRepository = channelRepository;
        _n11ChannelRepository = n11ChannelRepository;
        _countryRepository = countryRepository;
        _administrativeAreaRepository = administrativeAreaRepository;
        _localityRepository = localityRepository;
        _dataFilter = dataFilter;
        _n11OrderClient = n11OrderClient;
        _orderSyncManager = orderSyncManager;
        _snapshotBuilder = snapshotBuilder;
        _currentCompany = currentCompany;
        _reservationRepository = reservationRepository;
        _fulfillmentLinkRepository = fulfillmentLinkRepository;
        _reservationManager = reservationManager;
        _voucherRepository = voucherRepository;
        _voucherAppService = voucherAppService;
    }

    // ── Sipariş Fazı O3 — REZERVASYON (Faz 7). Stoğa dokunur; pazaryerine YAZMAZ. ───────────────────

    /// <summary>Siparişin rezervasyon görünümü. null = rezervasyon kaydı hiç açılmamış (sipariş bu özellikten
    /// önce çekilmiş olabilir — geriye dönük kurulmaz).</summary>
    public virtual async Task<OrderReservationDto?> GetReservationAsync(Guid orderId)
    {
        var reservation = await AsyncExecuter.FirstOrDefaultAsync(
            (await _reservationRepository.GetQueryableAsync()).Where(r => r.OrderId == orderId));
        if (reservation is null)
        {
            return null;
        }

        var links = await AsyncExecuter.ToListAsync(
            (await _fulfillmentLinkRepository.GetQueryableAsync()).Where(l => l.OrderId == orderId));

        return ToReservationDto(reservation, links);
    }

    /// <summary>İptal talebine KARAR verir. <b>Onay</b> rezervasyonu serbest bırakır (stok geri gelir),
    /// <b>red</b> tutmaya devam eder. Karar ile fiziksel etki AYNI transaction'da yürür — karar kaydedilip
    /// serbest bırakma yarıda kalırsa defter kararla tutarsız kalırdı.</summary>
    // ⚠ Stoğu GERİ VEREN karar. Sınıf düzeyindeki SalesChannels.Default (salt görüntüleme) bunu açık
    // bırakıyordu: rezervasyonu yalnız GÖRME yetkisi olan bir kullanıcı iptali onaylayıp madeni serbest
    // bırakabilirdi. Değiştirme yetkisi ister (emsal: AcceptOrderLineAsync).
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    [UnitOfWork(isTransactional: true)]
    public virtual async Task<OrderReservationDto> DecideCancellationAsync(OrderCancellationDecisionDto input)
    {
        var reservation = await AsyncExecuter.FirstOrDefaultAsync(
            (await _reservationRepository.GetQueryableAsync()).Where(r => r.OrderId == input.OrderId))
            ?? throw new BusinessException("TradeXpress:OrderReservation:NotFound");

        var now = Clock.Now.ToUniversalTime();
        if (input.Approve)
        {
            // Çıkış yapılmışsa entity guard'ı bloklar — artık iade sürecidir (2026-08-05 Hakan kararı).
            reservation.ApproveCancellation(CurrentUser.Id, now, input.Note);
            await _reservationRepository.UpdateAsync(reservation, autoSave: true);
            await _reservationManager.ReleaseAsync(input.OrderId, input.Note);
        }
        else
        {
            reservation.RejectCancellation(CurrentUser.Id, now, input.Note);
            await _reservationRepository.UpdateAsync(reservation, autoSave: true);
        }

        return await GetReservationAsync(input.OrderId)
               ?? throw new BusinessException("TradeXpress:OrderReservation:NotFound");
    }

    /// <summary>Rezervasyonu ELLE serbest bırakır (iptal talebi olmadan). Fiş satırları soft-delete edilir.
    /// <para>Aynı gerekçe: stoğu geri veren bir işlem salt-görüntüleme yetkisine açık kalamaz.</para></summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    [UnitOfWork(isTransactional: true)]
    public virtual async Task<OrderReservationDto> ReleaseReservationAsync(OrderReservationReleaseDto input)
    {
        // ⚠ SESSİZ NO-OP GUARD'I: OrderReservationManager.ReleaseAsync, Reserved OLMAYAN kaydı sessizce
        // atlar — bu İDEMPOTENT iç yol için doğrudur (iptal onayı iki kez işlense de stok bir kez döner).
        // Ama KULLANICI aksiyonunda yanlıştır: karşılanmış bir rezervasyonda "Serbest Bırak" hiçbir şey
        // yapmadan başarı döndürüyordu — kullanıcı stoğu geri aldığını sanırdı. Açık aksiyon açık cevap ister.
        var existing = await AsyncExecuter.FirstOrDefaultAsync(
            (await _reservationRepository.GetQueryableAsync()).Where(r => r.OrderId == input.OrderId))
            ?? throw new BusinessException("TradeXpress:OrderReservation:NotFound");

        if (existing.Status == OrderReservationStatus.Fulfilled)
        {
            throw new BusinessException("TradeXpress:OrderReservation:CannotReleaseFulfilled");
        }

        if (existing.Status != OrderReservationStatus.Reserved)
        {
            throw new BusinessException("TradeXpress:OrderReservation:NotReleasable")
                .WithData("Status", existing.Status);
        }

        await _reservationManager.ReleaseAsync(input.OrderId, input.Reason);
        return await GetReservationAsync(input.OrderId)
               ?? throw new BusinessException("TradeXpress:OrderReservation:NotFound");
    }

    private static OrderReservationDto ToReservationDto(
        OrderReservation reservation, List<OrderFulfillmentLink> links)
    {
        return new OrderReservationDto
        {
            OrderId                 = reservation.OrderId,
            Status                  = reservation.Status,
            CancellationDecision    = reservation.CancellationDecision,
            VoucherId               = reservation.VoucherId,
            ReservedAt              = reservation.ReservedAt,
            ReleasedAt              = reservation.ReleasedAt,
            CancellationRequestedAt = reservation.CancellationRequestedAt,
            CancellationDecidedAt   = reservation.CancellationDecidedAt,
            Note                    = reservation.Note,
            Links = links.ConvertAll(l => new OrderFulfillmentLinkDto
            {
                Id                = l.Id,
                RemoteLineId      = l.RemoteLineId,
                VoucherId         = l.VoucherId,
                VoucherLineId     = l.VoucherLineId,
                Kind              = l.Kind,
                FulfilledQuantity = l.FulfilledQuantity,
                FulfilledAmount   = l.FulfilledAmount,
                PriceDifference   = l.PriceDifference,
                Note              = l.Note,
            }),
        };
    }

    // ── "Karar Bekleyenler" sekmesi (2026-08-21 Hakan yerleşim kararı) — SALT OKUMA ──────────────────

    /// <summary>Karar bekleyenlerin TEK listesi: ① Blocked rezervasyonlar (gerekçesiyle) ② iptal talebi
    /// bekleyen siparişler ③ yaş eşiğini aşan aktif rezervler. Tip ayrımı DTO'daki <c>Kind</c> alanıyla —
    /// üç ayrı uç açılmadı (aynı soru: "burada benim yapmam gereken bir şey var mı?").
    /// <para><b>Hiçbir eksene YAZMAZ</b> — yaşlanan rezerv için de süre aşımı YOKTUR ("sipariş siparıştir");
    /// eşik yalnız görünürlük sağlar. Sayfalama yok: bekleyen iş listesi tanım gereği kısadır ve sekme rozeti
    /// toplam sayıyı ister.</para></summary>
    public virtual async Task<List<OrderPendingDecisionDto>> GetPendingDecisionsAsync()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            // Konsolide (şirketsiz) bağlamda boş liste: başka şirketin bekleyen işi gösterilmez
            // (OrderReservationInboxSummaryProvider ile aynı fail-closed kural).
            return new List<OrderPendingDecisionDto>();
        }

        var agingThreshold = Clock.Now.ToUniversalTime()
            .AddDays(-OrderConsts.AgingReservationThresholdDays);

        // ① iptal talebi karar bekliyor (stok ekseni ne olursa olsun — karar hâlâ insanın)
        // ② rezervasyon kurulamadı; iptali ONAYLANMIŞ kayıt hariç (sipariş kapandı, bağlanacak eşleşme kalmadı —
        //    süresiz listede kalması gürültü olurdu)
        // ③ eşikten eski AKTİF rezerv. Fulfilled/Released kayıtların işi bitmiştir, listeye girmez.
        var reservations = await AsyncExecuter.ToListAsync(
            (await _reservationRepository.GetQueryableAsync())
                .Where(r => r.CompanyId == companyId)
                .Where(r => r.CancellationDecision == OrderCancellationDecision.Pending
                            || (r.Status == OrderReservationStatus.Blocked
                                && r.CancellationDecision != OrderCancellationDecision.Approved)
                            || (r.Status == OrderReservationStatus.Reserved
                                && r.ReservedAt != null
                                && r.ReservedAt <= agingThreshold)));

        if (reservations.Count == 0)
        {
            return new List<OrderPendingDecisionDto>();
        }

        var orderIds = reservations.Select(r => r.OrderId).ToList();
        var orderById = (await AsyncExecuter.ToListAsync(
                (await _orderRepository.GetQueryableAsync())
                    .Where(o => o.CompanyId == companyId && orderIds.Contains(o.Id))))
            .ToDictionary(o => o.Id);

        var channelCodes = await GetChannelCodesAsync(
            companyId, orderById.Values.Select(o => o.SalesChannelId).Distinct().ToList());

        var rows = new List<OrderPendingDecisionDto>();
        foreach (var reservation in reservations)
        {
            // Rezervasyon katmanı siparişten AYRI yaşar (senkron Order'ı yeniden yazabilir) — sipariş
            // bulunamazsa satır üretilmez; kayıt silinmez, sipariş geri geldiğinde yeniden görünür.
            if (!orderById.TryGetValue(reservation.OrderId, out var order))
            {
                continue;
            }

            rows.Add(BuildPendingDecisionRow(reservation, order, channelCodes));
        }

        // En uzun süredir bekleyen ÜSTTE — sekmenin amacı önceliklendirme, tazelik değil.
        return rows.OrderBy(r => r.PendingSinceUtc).ThenBy(r => r.OrderId).ToList();
    }

    /// <summary>Satır kurucu. Tip seçimi ÖNCELİKLİDİR (iptal talebi &gt; kurulamadı &gt; yaşlandı): rozeti
    /// kullanıcıdan iş isteyen en acil durum belirler; ham iki eksen DTO'da ayrıca taşınır, bilgi gizlenmez.
    /// (Elle eşleme değil enrichment — reservation + order + kanal sözlüğü birleşir; Mapperly kapsamı dışı.)</summary>
    private static OrderPendingDecisionDto BuildPendingDecisionRow(
        OrderReservation reservation, Order order, IReadOnlyDictionary<Guid, string> channelCodes)
    {
        OrderPendingDecisionKind kind;
        DateTime pendingSince;
        if (reservation.CancellationDecision == OrderCancellationDecision.Pending)
        {
            kind = OrderPendingDecisionKind.CancellationRequested;
            // İLK talep anı (RequestCancellation tekrar eden kanal sinyalinde tazelemez) → yaş gerçek bekleme süresi.
            pendingSince = reservation.CancellationRequestedAt ?? reservation.CreationTime;
        }
        else if (reservation.Status == OrderReservationStatus.Blocked)
        {
            kind = OrderPendingDecisionKind.BlockedReservation;
            // Blocked'a özgü zaman damgası yok — kaydın açıldığı an beklemenin başlangıcıdır.
            pendingSince = reservation.CreationTime;
        }
        else
        {
            kind = OrderPendingDecisionKind.AgingReservation;
            pendingSince = reservation.ReservedAt ?? reservation.CreationTime;
        }

        return new OrderPendingDecisionDto
        {
            OrderId              = reservation.OrderId,
            Kind                 = kind,
            ReservationStatus    = reservation.Status,
            CancellationDecision = reservation.CancellationDecision,
            Reason               = reservation.Note,
            PendingSinceUtc      = pendingSince,
            ChannelType          = order.ChannelType,
            SalesChannelCode     = channelCodes.GetValueOrDefault(order.SalesChannelId),
            OrderNumber          = order.OrderNumber,
            OrderDate            = order.OrderDate,
            NeutralStatus        = order.NeutralStatus,
            CustomerName         = ResolveCustomerName(order),
            TotalAmount          = order.TotalAmount,
        };
    }

    // ── Ortak panel: birleşik liste (tüm kanallar) ────────────────────────────────────────────────────

    public virtual async Task<PagedResultDto<OrderListDto>> GetListAsync(OrderListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new PagedResultDto<OrderListDto>(0, new List<OrderListDto>());
        }

        // MASTER = SİPARİŞ (order düzeyinde sayfalı). DETAIL = siparişin kalemleri (master-detail grid), master satır
        // açılınca gösterilir. Şirket kapsamı (company-owned) SUNUCU zorlar; kanal/durum/tarih filtreleri + global arama
        // MERKEZİ whitelist'li motorla (ApplyListRequest) uygulanır — kolon filtresi sessizce düşmez.
        var orderQuery = (await _orderRepository.GetQueryableAsync())
            .Where(o => o.CompanyId == companyId)
            .ApplyListRequest(input, OrderListAllowedFields);

        // Client açık sıralama vermediyse alan-özel varsayılan sıralamayı uygula: order status → tarih (yeni→eski) → Id
        // (ApplyListRequest'in yalnız-Id fallback'ini ezer). Açık sıralama varsa merkezi motorun kararına dokunma.
        var hasExplicitSort = (input.Sorts is { Count: > 0 }) || !string.IsNullOrWhiteSpace(input.Sorting);
        if (!hasExplicitSort)
        {
            orderQuery = orderQuery
                .OrderBy(o => o.NeutralStatus).ThenByDescending(o => o.OrderDate).ThenBy(o => o.Id);
        }

        var totalCount = await AsyncExecuter.CountAsync(orderQuery);
        var orders = await AsyncExecuter.ToListAsync(orderQuery.ApplyPaging(input));

        // Sayfadaki siparişlerin TÜM kalemlerini TEK sorguda çek (N+1 yok), OrderId'ye grupla.
        var orderIds = orders.Select(o => o.Id).ToList();
        var lines = orderIds.Count == 0
            ? new List<OrderLine>()
            : await AsyncExecuter.ToListAsync(
                (await _orderLineRepository.GetQueryableAsync()).Where(l => orderIds.Contains(l.OrderId)));
        var linesByOrder = lines.GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var dtos = orders.Select(order =>
        {
            var dto = ObjectMapper.Map<Order, OrderListDto>(order);
            dto.CustomerName = ResolveCustomerName(order);
            if (linesByOrder.TryGetValue(order.Id, out var orderLines))
            {
                dto.Items = orderLines
                    .OrderBy(l => l.RemoteLineStatus).ThenBy(l => l.CreationTime).ThenBy(l => l.Id)
                    .Select(l => BuildItemRow(order, l))
                    .ToList();
            }

            return dto;
        }).ToList();
        await EnrichOrderChannelCodesAsync(companyId, dtos);

        return new PagedResultDto<OrderListDto>(totalCount, dtos);
    }

    /// <summary>Listede gösterilecek müşteri adı. Kanal "müşteri" alanını boş bırakabildiği için (N11'de sık)
    /// sırayla ALICI, sonra TESLİMAT alıcısı adına düşer — kolon boş kalmasın.
    ///
    /// <para>Ek sorgu doğurmaz: detay snapshot'ı sipariş satırında TEK JSON kolonu olarak tutuluyor, yani
    /// <c>order.Detail</c> zaten elde.</para></summary>
    private static string? ResolveCustomerName(Order order)
    {
        if (!string.IsNullOrWhiteSpace(order.CustomerName))
        {
            return order.CustomerName;
        }

        var buyerName = order.Detail?.Buyer?.FullName;
        if (!string.IsNullOrWhiteSpace(buyerName))
        {
            return buyerName;
        }

        return order.Detail?.ShippingAddress?.FullName;
    }

    /// <summary>Bir kalemi DETAIL grid satırına çevirir — line alanları (Mapperly) + kanal (durum etiketi kanal-farkında)
    /// + zengin kalem detayı (snapshot'tan RemoteLineId ile). Order-header alanları kalem DTO'sunda kullanılmaz.</summary>
    private OrderItemListDto BuildItemRow(Order order, OrderLine line)
    {
        var dto = ObjectMapper.Map<OrderLine, OrderItemListDto>(line);
        dto.ChannelType = order.ChannelType;
        ApplyItemDetail(dto, order, line);
        return dto;
    }

    public virtual async Task<OrderDto> GetAsync(Guid id)
    {
        var order = await GetOwnedOrderAsync(id);
        var dto = ObjectMapper.Map<Order, OrderDto>(order);

        var lines = await AsyncExecuter.ToListAsync(
            (await _orderLineRepository.GetQueryableAsync())
                .Where(l => l.OrderId == order.Id)
                .OrderBy(l => l.CreationTime).ThenBy(l => l.Id));
        dto.Lines = lines.Select(l => ObjectMapper.Map<OrderLine, OrderLineDto>(l)).ToList();

        var code = await ResolveChannelCodeAsync(order.SalesChannelId);
        dto.SalesChannelCode = code;

        // Editable alanlar — değer = düzeltme (OrderOperationalData) ?? orijinal (Order/Order.Detail). Kaydedilen
        // SADECE OrderOperationalData'ya gider; orijinal burada HİÇ değişmez (denetim kanıtı).
        var operational = await FindOperationalDataAsync(id);
        var buyer = order.Detail?.Buyer;
        dto.Buyer = new OrderEditPartyDto
        {
            FullName = operational?.BuyerCorrection?.FullName ?? buyer?.FullName,
            Email = operational?.BuyerCorrection?.Email ?? buyer?.Email,
            TcId = operational?.BuyerCorrection?.TcId ?? buyer?.TcId,
            TaxId = operational?.BuyerCorrection?.TaxId ?? buyer?.TaxId,
            TaxOffice = operational?.BuyerCorrection?.TaxOffice ?? buyer?.TaxOffice,
        };
        // Sipariş adresleri Türkiye kabul edilir → ülke TR-kilitli + il ADINDAN core idari-alanı ön-seç (picker
        // İl'i ön-seçer). İlçe/mahalle KASITLI ön-seçilmez (canlı N11 mahalle fetch'inden kaçınmak için) — ithal
        // İlçe/Mahalle ADLARI modelde korunur, Save'de yazılır. TR kataloğu yoksa CountryId null (picker serbest mod).
        var geography = await BuildTurkeyAddressCatalogAsync();
        dto.CountryId = geography.CountryId;
        dto.BillingAddress = MapEditAddress(operational?.BillingAddressCorrection, order.Detail?.BillingAddress, geography);
        dto.ShippingAddress = MapEditAddress(operational?.ShippingAddressCorrection, order.Detail?.ShippingAddress, geography);
        dto.CargoProvider = operational?.CargoProviderOverride ?? order.CargoProvider;
        dto.CargoTrackingNumber = operational?.CargoTrackingNumberOverride ?? order.CargoTrackingNumber;

        dto.PendingLineCount = order.ChannelType == SalesChannelType.TrN11
            ? await CountPendingLinesAsync(order.Id, lines)
            : 0;

        return dto;
    }

    /// <summary>Hâlâ Pending olan (N11'e hiç Kabul/Red bildirilmemiş) kalem sayısı — edit formu toolbar'ındaki
    /// Kabul Et/Reddet'in GÖRÜNÜRLÜĞÜ bunu okur. Operasyonel kaydı OLMAYAN kalem de Pending sayılır (henüz hiç
    /// aksiyon alınmamış demektir; <see cref="PrepareBulkActionAsync"/> ile AYNI varsayılan).</summary>
    private async Task<int> CountPendingLinesAsync(Guid orderId, List<OrderLine> lines)
    {
        var remoteLineIds = lines.Where(l => l.RemoteLineId != null).Select(l => l.RemoteLineId!).ToList();
        if (remoteLineIds.Count == 0)
        {
            return 0;
        }

        var actionedCount = await AsyncExecuter.CountAsync(
            (await _operationalLineRepository.GetQueryableAsync())
                .Where(x => x.OrderId == orderId && remoteLineIds.Contains(x.RemoteLineId)
                    && x.ActionStatus != OrderLineActionStatus.Pending));

        return remoteLineIds.Count - actionedCount;
    }

    /// <summary>Sipariş edit formunu kaydeder — SADECE OrderOperationalData'ya yazar (orijinal Order.Detail HİÇ
    /// değişmez). ICommitCoordinator sözleşmesi: taze OrderDto döner (Id her zaman dolu — Order'da Create yok).</summary>
    public virtual async Task<OrderDto> UpdateAsync(Guid id, OrderDto input)
    {
        await GetOwnedOrderAsync(id);   // sahiplik + varlık doğrulaması
        var operational = await FindOperationalDataAsync(id);
        if (operational is null)
        {
            operational = new OrderOperationalData(EnsureCurrentCompanyId(), id);
            await _operationalDataRepository.InsertAsync(operational, autoSave: false);
        }

        operational.CorrectBuyer(input.Buyer.FullName, input.Buyer.Email, input.Buyer.TcId, input.Buyer.TaxId, input.Buyer.TaxOffice);
        operational.CorrectBillingAddress(BuildOperationalAddress(input.BillingAddress));
        operational.CorrectShippingAddress(BuildOperationalAddress(input.ShippingAddress));
        operational.OverrideCargo(input.CargoProvider, input.CargoTrackingNumber);

        await _operationalDataRepository.UpdateAsync(operational);
        return await GetAsync(id);
    }

    // ── Çekim (salt GET → idempotent upsert) ──────────────────────────────────────────────────────────

    // Çekim ÇEKİRDEĞİ OrderSyncManager'da (streaming, order-başına-save, auth'suz; worker + bu AppService ortak kullanır).
    // Buradaki metodlar yalnız yetki + current-company scope ekleyip delege eder.

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<OrderFetchResultDto> FetchOrdersAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var report = new OrderFetchResultDto();
        await _orderSyncManager.SyncSingleChannelAsync(companyId, salesChannelId, report);
        report.ChannelsProcessed = 1;
        return report;
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<OrderFetchResultDto> FetchAllOrdersAsync()
    {
        var companyId = EnsureCurrentCompanyId();
        var report = new OrderFetchResultDto();
        await _orderSyncManager.SyncCompanyAsync(companyId, onlyEmpty: false, report);
        return report;
    }

    // ── Kalem edit satırları (OrderItemsDrill kaynağı) ────────────────────────────────────────────────

    public virtual async Task<List<OrderLineEditDto>> GetOrderLineEditsAsync(Guid orderId)
    {
        var order = await GetOwnedOrderAsync(orderId);
        var lines = await AsyncExecuter.ToListAsync(
            (await _orderLineRepository.GetQueryableAsync())
                .Where(l => l.OrderId == orderId)
                .OrderBy(l => l.CreationTime).ThenBy(l => l.Id));

        var remoteLineIds = lines.Where(l => l.RemoteLineId != null).Select(l => l.RemoteLineId!).ToList();
        var operationalRows = remoteLineIds.Count == 0
            ? new List<OrderLineOperationalData>()
            : await AsyncExecuter.ToListAsync(
                (await _operationalLineRepository.GetQueryableAsync())
                    .Where(x => x.OrderId == orderId && remoteLineIds.Contains(x.RemoteLineId)));
        var operationalByRemoteId = operationalRows.ToDictionary(x => x.RemoteLineId, x => x);

        return lines
            .Select(line => BuildLineEditDto(order, line, operationalByRemoteId.GetValueOrDefault(line.RemoteLineId ?? string.Empty)))
            .ToList();
    }

    public virtual async Task<OrderLineEditDto> SaveOrderLineEditAsync(OrderLineEditDto input)
    {
        var order = await GetOwnedOrderAsync(input.OrderId);
        var operational = await AsyncExecuter.FirstOrDefaultAsync(
            (await _operationalLineRepository.GetQueryableAsync())
                .Where(x => x.OrderId == input.OrderId && x.RemoteLineId == input.RemoteLineId));
        if (operational is null)
        {
            operational = new OrderLineOperationalData(EnsureCurrentCompanyId(), input.OrderId, input.RemoteLineId);
            await _operationalLineRepository.InsertAsync(operational, autoSave: false);
        }

        var now = Clock.Now.ToUniversalTime();
        foreach (var customText in input.CustomTexts)
        {
            if (string.IsNullOrWhiteSpace(customText.CorrectedText))
            {
                operational.ClearCustomTextCorrection(customText.Option);
            }
            else
            {
                operational.CorrectCustomText(customText.Option, customText.CorrectedText, now);
            }
        }

        if (input.ProductVariantId != operational.ProductVariantId)
        {
            await ApplyProductMatchAsync(operational, input.ProductVariantId, now);
        }

        await _operationalLineRepository.UpdateAsync(operational);

        var line = await AsyncExecuter.FirstOrDefaultAsync(
            (await _orderLineRepository.GetQueryableAsync())
                .Where(l => l.OrderId == input.OrderId && l.RemoteLineId == input.RemoteLineId));
        if (line is null)
        {
            throw new BusinessException("TradeXpress:Order:LineNotFound");
        }

        return BuildLineEditDto(order, line, operational);
    }

    // ── Sipariş Fazı O2 — state machine aksiyonları (N11'e YAZAR — GERÇEK, geri alınamaz; yalnız N11 kanalı) ──

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<OrderLineEditDto> AcceptOrderLineAsync(OrderLineAcceptDto input)
    {
        var (order, channel, operational, itemId) = await PrepareActionAsync(input.OrderId, input.RemoteLineId);
        await _n11OrderClient.AcceptOrderItemAsync(channel.AppKey, channel.AppSecret, new[] { itemId }, input.NumberOfPackages);

        var now = Clock.Now.ToUniversalTime();
        operational.MarkAccepted(now);
        await _operationalLineRepository.UpdateAsync(operational);

        return await RebuildLineEditDtoAsync(order, input.RemoteLineId, operational);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<OrderLineEditDto> RejectOrderLineAsync(OrderLineRejectDto input)
    {
        var (order, channel, operational, itemId) = await PrepareActionAsync(input.OrderId, input.RemoteLineId);
        await _n11OrderClient.RejectOrderItemAsync(channel.AppKey, channel.AppSecret, new[] { itemId }, input.Reason);

        var now = Clock.Now.ToUniversalTime();
        operational.MarkRejected(input.Reason, now);
        await _operationalLineRepository.UpdateAsync(operational);

        return await RebuildLineEditDtoAsync(order, input.RemoteLineId, operational);
    }

    // ── Sipariş edit formu toolbar'ı — TÜM bekleyen kalemleri TEK N11 isteğiyle işler (WSDL orderItemList zaten
    // liste; siparişin tüm kalemleri aynı pakette gönderilirken bu N11'in kendi doğal biçimidir). ──

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<OrderBulkActionResultDto> AcceptOrderAsync(OrderAcceptDto input)
    {
        var (channel, pending) = await PrepareBulkActionAsync(input.OrderId);
        if (pending.Count == 0)
        {
            return new OrderBulkActionResultDto { AffectedCount = 0 };
        }

        await _n11OrderClient.AcceptOrderItemAsync(
            channel.AppKey, channel.AppSecret, pending.Select(p => p.ItemId).ToList(), input.NumberOfPackages);

        var now = Clock.Now.ToUniversalTime();
        foreach (var (operational, _) in pending)
        {
            operational.MarkAccepted(now);
            await _operationalLineRepository.UpdateAsync(operational);
        }

        return new OrderBulkActionResultDto { AffectedCount = pending.Count };
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<OrderBulkActionResultDto> RejectOrderAsync(OrderRejectDto input)
    {
        var (channel, pending) = await PrepareBulkActionAsync(input.OrderId);
        if (pending.Count == 0)
        {
            return new OrderBulkActionResultDto { AffectedCount = 0 };
        }

        await _n11OrderClient.RejectOrderItemAsync(
            channel.AppKey, channel.AppSecret, pending.Select(p => p.ItemId).ToList(), input.Reason);

        var now = Clock.Now.ToUniversalTime();
        foreach (var (operational, _) in pending)
        {
            operational.MarkRejected(input.Reason, now);
            await _operationalLineRepository.UpdateAsync(operational);
        }

        return new OrderBulkActionResultDto { AffectedCount = pending.Count };
    }

    /// <summary>Toplu aksiyon ortak hazırlığı: sahiplik + N11 kanal guard'ı + kanal kimlik bilgisi + siparişin TÜM
    /// kalemleri için operasyonel kayıt (yoksa oluşturulur) + yalnız HÂLÂ Pending olanlar (N11 id'siyle) süzülür.</summary>
    private async Task<(SalesChannelTrN11 Channel, List<(OrderLineOperationalData Operational, long ItemId)> Pending)> PrepareBulkActionAsync(Guid orderId)
    {
        var order = await GetOwnedOrderAsync(orderId);
        if (order.ChannelType != SalesChannelType.TrN11)
        {
            throw new BusinessException("TradeXpress:Order:ActionsOnlySupportedForN11");
        }

        var channel = await _n11ChannelRepository.FindAsync(order.SalesChannelId);
        if (channel is null)
        {
            throw new BusinessException("TradeXpress:Order:ChannelNotFound");
        }

        var lines = await AsyncExecuter.ToListAsync(
            (await _orderLineRepository.GetQueryableAsync())
                .Where(l => l.OrderId == orderId && l.RemoteLineId != null));

        var remoteLineIds = lines.Select(l => l.RemoteLineId!).ToList();
        var operationalRows = remoteLineIds.Count == 0
            ? new List<OrderLineOperationalData>()
            : await AsyncExecuter.ToListAsync(
                (await _operationalLineRepository.GetQueryableAsync())
                    .Where(x => x.OrderId == orderId && remoteLineIds.Contains(x.RemoteLineId)));
        var operationalByRemoteId = operationalRows.ToDictionary(x => x.RemoteLineId, x => x);

        var pending = new List<(OrderLineOperationalData Operational, long ItemId)>();
        foreach (var line in lines)
        {
            var remoteLineId = line.RemoteLineId!;
            if (!long.TryParse(remoteLineId, out var itemId))
            {
                continue;   // biçimsiz kanal id'si — sessizce atla (tekil aksiyon zaten bunu tipli hatayla fırlatır)
            }

            var operational = operationalByRemoteId.GetValueOrDefault(remoteLineId);
            if (operational is null)
            {
                operational = new OrderLineOperationalData(EnsureCurrentCompanyId(), orderId, remoteLineId);
                await _operationalLineRepository.InsertAsync(operational, autoSave: false);
            }

            if (operational.ActionStatus == OrderLineActionStatus.Pending)
            {
                pending.Add((operational, itemId));
            }
        }

        return (channel, pending);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<OrderLineEditDto> ShipOrderLineAsync(OrderLineShipDto input)
    {
        var (order, channel, operational, itemId) = await PrepareActionAsync(input.OrderId, input.RemoteLineId);
        await _n11OrderClient.MakeShipmentAsync(
            channel.AppKey, channel.AppSecret, itemId,
            input.ShipmentCompanyId, input.TrackingNumber, input.CampaignNumber, input.ShipmentMethod);

        var now = Clock.Now.ToUniversalTime();
        operational.MarkShipped(now);
        await _operationalLineRepository.UpdateAsync(operational);

        return await RebuildLineEditDtoAsync(order, input.RemoteLineId, operational);
    }

    /// <summary>Aksiyon ortak hazırlığı: sahiplik + N11 kanal guard'ı + kanal kimlik bilgisi + operasyonel kayıt
    /// (yoksa oluşturulur) + RemoteLineId'nin N11 sayısal id'sine çözümü (Accept/Reject/Shipment isteği budur).</summary>
    private async Task<(Order Order, SalesChannelTrN11 Channel, OrderLineOperationalData Operational, long ItemId)> PrepareActionAsync(
        Guid orderId, string remoteLineId)
    {
        var order = await GetOwnedOrderAsync(orderId);
        if (order.ChannelType != SalesChannelType.TrN11)
        {
            throw new BusinessException("TradeXpress:Order:ActionsOnlySupportedForN11");
        }

        if (!long.TryParse(remoteLineId, out var itemId))
        {
            throw new BusinessException("TradeXpress:Order:InvalidRemoteLineId").WithData("RemoteLineId", remoteLineId);
        }

        var channel = await _n11ChannelRepository.FindAsync(order.SalesChannelId);
        if (channel is null)
        {
            throw new BusinessException("TradeXpress:Order:ChannelNotFound");
        }

        var operational = await AsyncExecuter.FirstOrDefaultAsync(
            (await _operationalLineRepository.GetQueryableAsync())
                .Where(x => x.OrderId == orderId && x.RemoteLineId == remoteLineId));
        if (operational is null)
        {
            operational = new OrderLineOperationalData(EnsureCurrentCompanyId(), orderId, remoteLineId);
            await _operationalLineRepository.InsertAsync(operational, autoSave: true);
        }

        return (order, channel, operational, itemId);
    }

    private async Task<OrderLineEditDto> RebuildLineEditDtoAsync(Order order, string remoteLineId, OrderLineOperationalData operational)
    {
        var line = await AsyncExecuter.FirstOrDefaultAsync(
            (await _orderLineRepository.GetQueryableAsync())
                .Where(l => l.OrderId == order.Id && l.RemoteLineId == remoteLineId));
        if (line is null)
        {
            throw new BusinessException("TradeXpress:Order:LineNotFound");
        }

        return BuildLineEditDto(order, line, operational);
    }

    /// <summary>Rezervasyonu FİZİKİ ÇIKIŞA çevirir (hazırlayan kasa).
    ///
    /// <para><b>Neden <c>IVoucherAppService.SaveLineAsync</c> kullanılıyor</b> (rezervasyon fişi yazan
    /// materializer'ın aksine): burada GERÇEK bir kullanıcı var. O yol kasa yetkisini doğrular, poster'ları
    /// çalıştırır ve stok tetiğini yayımlar — üçünü de bedavaya almak, worker bağlamı için yazılmış özel yolu
    /// kullanıcı bağlamına taşımaktan iyidir. Materializer'ın "SaveLineAsync KULLANILMAZ" notu YALNIZ kullanıcısız
    /// worker içindir.</para>
    ///
    /// <para><b>ÇİFT SAYIM GUARD'I:</b> fiziki çıkış satırları yazılır VE rezervasyon satırları aynı transaction'da
    /// soft-delete edilir. İkincisi unutulursa aynı mal iki kez düşer (<c>Available</c> 30 yerine 10 olur) ve
    /// ürün stokta olduğu hâlde satıştan kalkar — hiçbir istisna doğmaz.</para></summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    [UnitOfWork(isTransactional: true)]
    public virtual async Task<OrderReservationDto> FulfillReservationAsync(OrderFulfillmentInputDto input)
    {
        var reservation = await AsyncExecuter.FirstOrDefaultAsync(
            (await _reservationRepository.GetQueryableAsync()).Where(r => r.OrderId == input.OrderId))
            ?? throw new BusinessException("TradeXpress:OrderReservation:NotFound");

        // Guard entity'de de var (MustBeReservedToFulfill) ama burada ERKEN durmak, fiş yazmaya başlayıp
        // yarıda kalmaktan iyidir.
        if (reservation.Status != OrderReservationStatus.Reserved)
        {
            throw new BusinessException("TradeXpress:OrderReservation:MustBeReservedToFulfill")
                .WithData("Status", reservation.Status);
        }

        if (reservation.VoucherId is not { } reservationVoucherId)
        {
            throw new BusinessException("TradeXpress:OrderReservation:NoLines");
        }

        var voucher = await _voucherRepository.GetAsync(reservationVoucherId);
        await _voucherRepository.EnsureCollectionLoadedAsync(voucher, v => v.Lines);

        var links = await AsyncExecuter.ToListAsync(
            (await _fulfillmentLinkRepository.GetQueryableAsync())
                .Where(l => l.OrderId == input.OrderId && l.Kind == OrderFulfillmentLinkKind.Reservation));

        var now = Clock.Now.ToUniversalTime();
        var declarationByLink = input.Lines.ToDictionary(l => l.FulfillmentLinkId);

        foreach (var reservedLine in voucher.Lines.Where(l => !l.IsDeleted).ToList())
        {
            // ① FİZİKİ ÇIKIŞ satırı — hazırlayan kasada, NORMAL ödeme tipiyle (fiziksel Net'e girer).
            var exitLine = await _voucherAppService.SaveLineAsync(new VoucherLineDto
            {
                BranchId      = input.BranchId,
                VaultId       = input.VaultId,
                AccountId     = voucher.AccountId,
                SubAccountId  = voucher.SubAccountId,
                Type          = reservedLine.Type,
                Direction     = ProcessDirectionType.Outbound,
                PaymentType   = ProcessPaymentType.Normal,
                CommodityId   = reservedLine.CommodityId,
                CommodityCode = reservedLine.CommodityCode,
                VariantId     = reservedLine.VariantId,
                Quantity      = reservedLine.Quantity,
                Amount        = reservedLine.Amount,
                Factor        = reservedLine.Factor,
                Total         = reservedLine.Total,
                MainUnitId    = reservedLine.MainUnitId,
                Description   = input.Note,
            });

            // ② Bağ kaydı: hangi çıkış satırı hangi sipariş kalemini karşıladı.
            var sourceLink = links.FirstOrDefault(l => l.VoucherLineId == reservedLine.Id);
            var exitLink = new OrderFulfillmentLink(
                voucher.CompanyId, input.OrderId,
                sourceLink?.RemoteLineId ?? string.Empty,
                exitLine.VoucherId!.Value, exitLine.Id,
                OrderFulfillmentLinkKind.PhysicalExit);
            exitLink.SetFulfilled(reservedLine.Quantity, reservedLine.Amount);

            // Fiyat farkı YALNIZ beyan edilmişse yazılır — null ile 0 arasındaki fark korunur.
            if (sourceLink is { } source && declarationByLink.TryGetValue(source.Id, out var declaration))
            {
                exitLink.DeclarePriceDifference(
                    declaration.PriceDifference, declaration.PriceDifferenceUnitId, declaration.Note);
            }

            await _fulfillmentLinkRepository.InsertAsync(exitLink, autoSave: true);
        }

        // ③ Rezervasyon satırlarını DÜŞÜR — çift sayımın tek panzehiri. DeleteLineAsync yolu stok tetiğini
        //    de yayımlar (Release'in eksik ETO sorunu bu yola bulaşmaz).
        foreach (var reservedLine in voucher.Lines.Where(l => !l.IsDeleted).ToList())
        {
            await _voucherAppService.DeleteLineAsync(voucher.Id, reservedLine.Id, input.Note ?? string.Empty);
        }

        // ④ Dönüşü olmayan nokta.
        reservation.MarkFulfilled(now);
        await _reservationRepository.UpdateAsync(reservation, autoSave: true);

        return await GetReservationAsync(input.OrderId)
               ?? throw new BusinessException("TradeXpress:OrderReservation:NotFound");
    }

    /// <summary>İADE GİRİŞİ — mal fiziksel olarak kasaya girdiğinde.
    ///
    /// <para><b>Stok yalnız burada döner.</b> Kanaldaki "iade talep edildi" / "kargoda iade" statüleri stoğa
    /// DOKUNMAZ: mal elimize geçmeden satılabilir göstermek, müşterinin onu ikinci kez satın alabilmesi
    /// demektir. Sistem sinyali görünür kılar; girişi insan kaydeder.</para>
    ///
    /// <para><b>Rezervasyona dokunulmaz</b> (<c>Fulfilled</c> kalır): iade rezervasyonu diriltseydi stok İKİ
    /// KEZ artardı — bir kez giriş fişiyle, bir kez rezervasyonun serbest kalmasıyla.</para>
    ///
    /// <para><b>Yeni entity YOK:</b> iade kaydı = giriş fişi + <c>Return</c> bağı. Rezervasyonun "ayrı yaşayan
    /// katman" felsefesinin aynısı.</para></summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    [UnitOfWork(isTransactional: true)]
    public virtual async Task<OrderReturnEntryResultDto> RegisterReturnEntryAsync(OrderReturnEntryDto input)
    {
        var result = new OrderReturnEntryResultDto();

        if (input.Lines.Count == 0)
        {
            throw new BusinessException("TradeXpress:OrderReturn:NoLines");
        }

        var exitLinks = await AsyncExecuter.ToListAsync(
            (await _fulfillmentLinkRepository.GetQueryableAsync())
                .Where(l => l.OrderId == input.OrderId && l.Kind == OrderFulfillmentLinkKind.PhysicalExit));

        if (exitLinks.Count == 0)
        {
            // İade tanımı gereği ÇIKIŞ SONRASIDIR. Çıkış yoksa geri gelecek bir şey de yoktur — sessizce
            // fiş yazmak, hiç çıkmamış malı stoğa eklemek olurdu.
            throw new BusinessException("TradeXpress:OrderReturn:NoPhysicalExit");
        }

        Guid? voucherId = null;

        foreach (var line in input.Lines)
        {
            var exitLink = exitLinks.FirstOrDefault(l => l.Id == line.PhysicalExitLinkId);
            if (exitLink is null)
            {
                result.Issues.Add($"Çıkış bağı bulunamadı: {line.PhysicalExitLinkId}");
                continue;
            }

            if (line.Quantity <= 0m && line.Amount <= 0m)
            {
                result.Issues.Add($"Miktarı sıfır olan satır atlandı: {exitLink.RemoteLineId}");
                continue;
            }

            // Emtia bilgisi ÇIKIŞ fişinin satırından okunur — operatör neyin geri geldiğini yeniden
            // seçmez; iade, çıkmış olan malın dönüşüdür.
            var exitVoucher = await _voucherRepository.GetAsync(exitLink.VoucherId);
            await _voucherRepository.EnsureCollectionLoadedAsync(exitVoucher, v => v.Lines);
            var exitLine = exitVoucher.Lines.FirstOrDefault(l => l.Id == exitLink.VoucherLineId);
            if (exitLine is null)
            {
                result.Issues.Add($"Çıkış fiş satırı bulunamadı: {exitLink.RemoteLineId}");
                continue;
            }

            var entryLine = await _voucherAppService.SaveLineAsync(new VoucherLineDto
            {
                Id            = Guid.Empty,
                VoucherId     = voucherId,   // ilk satır fişi açar, sonrakiler AYNI fişe biner
                BranchId      = input.BranchId,
                VaultId       = input.VaultId,
                AccountId     = exitVoucher.AccountId,
                SubAccountId  = exitVoucher.SubAccountId,
                Type          = exitLine.Type,
                Direction     = ProcessDirectionType.Inbound,
                PaymentType   = ProcessPaymentType.Return,
                CommodityId   = exitLine.CommodityId,
                CommodityCode = exitLine.CommodityCode,
                VariantId     = exitLine.VariantId,
                Quantity      = line.Quantity,
                Amount        = line.Amount,
                Factor        = exitLine.Factor,
                Total         = line.Amount * exitLine.Factor,
                MainUnitId    = exitLine.MainUnitId,
                Description   = input.Note,
            });

            voucherId ??= entryLine.VoucherId;

            var returnLink = new OrderFulfillmentLink(
                exitVoucher.CompanyId, input.OrderId, exitLink.RemoteLineId,
                entryLine.VoucherId!.Value, entryLine.Id, OrderFulfillmentLinkKind.Return);
            returnLink.SetFulfilled(line.Quantity, line.Amount);
            await _fulfillmentLinkRepository.InsertAsync(returnLink, autoSave: true);

            result.RegisteredLines++;
        }

        if (voucherId is not { } written)
        {
            throw new BusinessException("TradeXpress:OrderReturn:NothingRegistered")
                .WithData("Issues", string.Join(" · ", result.Issues));
        }

        result.VoucherId = written;
        return result;
    }

    /// <summary>Elle eşleştirme adayları — ÇALIŞILAN ŞİRKETİN ürün varyantları.
    ///
    /// <para><b>Şirket sınırı açıkça uygulanır:</b> <c>EntityVariant</c> kendi <c>CompanyId</c>'sini taşır ama
    /// aday listesi kullanıcının GÖRDÜĞÜ bir listedir — global filtreye ek olarak koşulu yazmak, bağlam
    /// kurulmamış bir çağrıda yabancı şirketin ürünlerinin listelenmesini yapısal olarak engeller.</para>
    ///
    /// <para>Arama hem ürün kodunda/adında hem varyant kodunda yapılır: kullanıcı elindeki pazaryeri stok
    /// koduna en çok neyin benzediğini arar, hangi alanda tutacağını önceden bilemez.</para></summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<List<OrderLineMatchCandidateDto>> GetLineMatchCandidatesAsync(
        OrderLineMatchCandidateRequestDto input)
    {
        var companyId = CompanyOwnershipGuard.ResolveOwnerCompanyId(_currentCompany);

        var variants = (await _productVariantRepository.GetQueryableAsync())
            .Where(v => v.EntityName == ProductEntityName && v.CompanyId == companyId && v.IsActive);

        var products = (await _productRepository.GetQueryableAsync())
            .Where(p => p.CompanyId == companyId);

        var query = from variant in variants
                    join product in products on variant.EntityId equals product.Id
                    select new OrderLineMatchCandidateDto
                    {
                        EntityVariantId = variant.Id,
                        ProductCode = product.Code,
                        ProductName = product.Name,
                        VariantCode = variant.Code,
                    };

        if (!string.IsNullOrWhiteSpace(input.Search))
        {
            var term = input.Search.Trim();
            query = query.Where(c =>
                c.ProductCode.Contains(term)
                || c.ProductName.Contains(term)
                || c.VariantCode.Contains(term));
        }

        var take = input.MaxCount <= 0 ? 50 : Math.Min(input.MaxCount, 200);
        var candidates = await AsyncExecuter.ToListAsync(
            query.OrderBy(c => c.ProductCode).ThenBy(c => c.VariantCode).Take(take));

        foreach (var candidate in candidates)
        {
            candidate.DisplayText = $"{candidate.ProductCode} · {candidate.ProductName} ({candidate.VariantCode})";
        }

        return candidates;
    }

    private async Task ApplyProductMatchAsync(OrderLineOperationalData operational, Guid? productVariantId, DateTime matchedAt)
    {
        if (productVariantId is not { } variantId)
        {
            operational.SetProductMatch(null, null, null, matchedAt);
            return;
        }

        var variant = await _productVariantRepository.FindAsync(variantId);
        if (variant is null)
        {
            throw new BusinessException("TradeXpress:Order:ProductVariantNotFound");
        }

        var (name, imageUrl) = await _snapshotBuilder.BuildAsync(variant);
        operational.SetProductMatch(variantId, name, imageUrl, matchedAt);
    }

    private static OrderLineEditDto BuildLineEditDto(Order order, OrderLine line, OrderLineOperationalData? operational)
    {
        var detailItem = order.Detail?.Items.FirstOrDefault(i =>
            !string.IsNullOrEmpty(i.RemoteLineId) && i.RemoteLineId == line.RemoteLineId);

        var hasDiscount = detailItem?.MallDiscount is not null || detailItem?.SellerDiscount is not null;
        var dto = new OrderLineEditDto
        {
            RemoteLineId = line.RemoteLineId ?? string.Empty,
            OrderId = order.Id,
            ProductName = detailItem?.ProductName ?? line.ProductNameSnapshot,
            ProductSellerCode = detailItem?.ProductSellerCode ?? line.StockCode,
            Quantity = line.Quantity,
            Price = detailItem?.Price ?? line.UnitPrice,
            Commission = detailItem?.Commission,
            DiscountTotal = hasDiscount ? (detailItem!.MallDiscount ?? 0m) + (detailItem.SellerDiscount ?? 0m) : null,
            Status = detailItem?.Status ?? line.RemoteLineStatus,
            Attributes = FormatAttributes(detailItem?.Attributes ?? new List<OrderDetailItemAttribute>()),
            ShipmentCompany = detailItem?.ShipmentCompany,
            TrackingNumber = detailItem?.TrackingNumber,
            ProductVariantId = operational?.ProductVariantId,
            ProductSnapshotName = operational?.ProductSnapshotName,
            ProductSnapshotImageUrl = operational?.ProductSnapshotImageUrl,
            MatchedAt = operational?.MatchedAt,
            ActionStatus = operational?.ActionStatus ?? OrderLineActionStatus.Pending,
            RejectReason = operational?.RejectReason,
            ActionAt = operational?.ActionAt,
        };

        var originalTexts = detailItem?.CustomTexts ?? new List<OrderDetailItemCustomText>();
        var corrections = operational?.CustomTextCorrections ?? new List<OrderLineCustomTextCorrection>();
        dto.CustomTexts = originalTexts.Select(t => new OrderLineCustomTextEditDto
        {
            Option = t.Option ?? string.Empty,
            OriginalText = t.Text,
            CorrectedText = corrections.FirstOrDefault(c => string.Equals(c.Option, t.Option, StringComparison.OrdinalIgnoreCase))?.CorrectedText,
        }).ToList();

        return dto;
    }

    // Kalıcı taraf (correction ?? original) isim-tabanlı ADLARI editable DTO'ya taşır + TR il ADINDAN core
    // idari-alanı en-iyi-çaba ön-seçer (picker İl'i ön-seçsin). İLÇE/MAHALLE KASITLI ön-seçilmez (LocalityId null →
    // picker canlı N11 mahalle fetch'i TETİKLEMEZ; sipariş açılışı hızlı + N11-bağımsız). İthal İl/İlçe/Mahalle ADLARI
    // her hâlde KORUNUR (eşleşme yoksa yalnız geo-id null, ad korunur — tolerant snapshot, ithal değer kaybolmaz).
    private static OrderEditAddressDto MapEditAddress(
        OrderOperationalAddress? correction, OrderDetailAddress? original, TurkeyAddressCatalog catalog)
    {
        var dto = new OrderEditAddressDto
        {
            FullName = correction?.FullName ?? original?.FullName,
            Line = correction?.Line ?? original?.Line ?? string.Empty,
            Neighborhood = correction?.Neighborhood ?? original?.Neighborhood,
            District = correction?.District ?? original?.District,
            City = correction?.City ?? original?.City ?? string.Empty,
            PostalCode = correction?.PostalCode ?? original?.PostalCode,
            Gsm = correction?.Gsm ?? original?.Gsm,
            TcId = correction?.TcId ?? original?.TcId,
            TaxId = correction?.TaxId ?? original?.TaxId,
            TaxOffice = correction?.TaxOffice ?? original?.TaxOffice,
            CountryCode = "TR",
        };

        var area = catalog.MatchArea(dto.City);
        if (area is not null)
        {
            // İl + İlçe ön-seçilir (geo-ref'ler picker ön-seçimi içindir — order'a PERSIST EDİLMEZ). Mahalle saklanmaz
            // (canlı N11) → picker, kayıtlı mahalle ADIYLA eşler. Adres popup içeriği DxPopup'ta TEMBEL render edildiğinden
            // canlı N11 mahalle çekimi yalnız kullanıcı adresi AÇINCA olur (sipariş açılışı başına DEĞİL — hız korunur).
            dto.AdministrativeAreaId = area.Id;
            dto.CityCode = area.Code;
            dto.AdministrativeAreaIsoCode = area.IsoCode;

            var locality = catalog.MatchLocality(area.Id, dto.District);
            if (locality is not null)
            {
                dto.LocalityId = locality.Id;
                dto.DistrictCode = locality.Code;
            }
        }

        // N11 "address" metni mahalleyi başta TEKRARLAR ("Oruçreis Mh. Atişalani..." + ayrıca neighborhood alanı) →
        // açık adresten mahalle ön-ekini sıyır (Mahalle combo/alanında zaten var — kullanıcı kararı, tekrar gösterme).
        dto.Line = StripNeighborhoodPrefix(dto.Line, dto.Neighborhood);

        return dto;
    }

    /// <summary>Açık adresin başındaki mahalle tekrarını sıyırır: satır mahalle adıyla (ve varsa ardından gelen
    /// "Mahallesi/Mah./Mh." son ekiyle) başlıyorsa o ön-ek atılır. Sıyırma satırı BOŞ bırakacaksa orijinal korunur
    /// (tek adres metnini yok etme — tolerant).</summary>
    private static string StripNeighborhoodPrefix(string line, string? neighborhood)
    {
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(neighborhood))
        {
            return line;
        }

        var trimmedLine = line.TrimStart();
        var prefix = neighborhood.Trim();
        if (!trimmedLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return line;
        }

        var remainder = trimmedLine[prefix.Length..].TrimStart();

        // Mahalle alanı son eksizse ("Oruçreis") ama satırda son ek varsa ("Oruçreis Mh. ...") artık son eki de at.
        foreach (var suffix in new[] { "Mahallesi", "Mah.", "Mh.", "Mah", "Mh" })
        {
            if (remainder.StartsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && (remainder.Length == suffix.Length || char.IsWhiteSpace(remainder[suffix.Length])))
            {
                remainder = remainder[suffix.Length..].TrimStart();
                break;
            }
        }

        return remainder.Length == 0 ? line : remainder;
    }

    // Kalıcı tarafa (OrderOperationalAddress) YALNIZ ADLAR yazılır — geo-ref id'leri (AdministrativeAreaId/LocalityId)
    // yalnız picker ön-seçimi içindir, PERSIST EDİLMEZ (kalıcı taraf isim-tabanlı + tolerant kalır). City/Line artık
    // zorunlu-string (IAddressEditModel) → boş değerler null'a çevrilir (tolerant snapshot'ta "" yerine null korunur).
    private static OrderOperationalAddress BuildOperationalAddress(OrderEditAddressDto dto)
    {
        return new OrderOperationalAddress(
            dto.FullName, NullIfBlank(dto.Line), dto.Neighborhood, dto.District, NullIfBlank(dto.City),
            dto.PostalCode, dto.Gsm, dto.TcId, dto.TaxId, dto.TaxOffice);
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // Sipariş adres picker'ının TR ÖN-SEÇİM kataloğunu kurar: host coğrafyadan TR ülke id'si + il isim→id haritası.
    // TR illeri eager-seed'li (GeographySeeder, N11'den) → host-global repo okuması (IMultiTenant DEĞİL; N11 TETİKLEMEZ).
    private async Task<TurkeyAddressCatalog> BuildTurkeyAddressCatalogAsync()
    {
        // TR ülkesi host satırı (Code=="TR", TenantId=null — picker'ın CountryAppService host kataloğuyla aynı satır).
        // Country IMultiTenant → tenant bağlamında host satırını görmek için filtre kapatılır (GeographyAppService deseni).
        Guid? countryId;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            countryId = await AsyncExecuter.FirstOrDefaultAsync(
                (await _countryRepository.GetQueryableAsync())
                    .Where(c => c.Code == "TR" && c.TenantId == null)
                    .Select(c => (Guid?)c.Id));
        }

        if (countryId is not { } trCountryId)
        {
            return TurkeyAddressCatalog.Empty; // TR kataloğu yok → picker serbest-ülke moduna düşer (güvenli geri-dönüş)
        }

        var areas = await AsyncExecuter.ToListAsync(
            (await _administrativeAreaRepository.GetQueryableAsync())
                .Where(a => a.CountryId == trCountryId)
                .Select(a => new AdministrativeAreaMatch(a.Id, a.Name, a.Code, a.Iso3166_2Code)));

        // İlçeler (TR ~970 satır, tek indeksli sorgu; eager-seed'li) — İlçe ön-seçimiyle popup'taki picker İlçe
        // combosunu doğru gösterir + mahalle combosu (canlı N11) yalnız POPUP AÇILINCA yüklenir (DxPopup tembel render).
        var localities = await AsyncExecuter.ToListAsync(
            (await _localityRepository.GetQueryableAsync())
                .Where(l => l.CountryId == trCountryId)
                .Select(l => new LocalityMatch(l.Id, l.AdministrativeAreaId, l.Name, l.Code)));

        return new TurkeyAddressCatalog(trCountryId, areas, localities);
    }

    private async Task<OrderOperationalData?> FindOperationalDataAsync(Guid orderId)
    {
        return await AsyncExecuter.FirstOrDefaultAsync(
            (await _operationalDataRepository.GetQueryableAsync()).Where(x => x.OrderId == orderId));
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Melez satırı, siparişin zengin detay snapshot'ındaki EŞLEŞEN kalemle (RemoteLineId) zenginleştirir —
    /// master-detail satırında bu kaleme özel komisyon/indirim/kargo/nitelik/tarihler. Detay yoksa (snapshot null /
    /// eşleşme yok) ItemDetail null kalır (panel "detay yok" gösterir). Mapperly değil (snapshot OrderLine'da değil).</summary>
    private static void ApplyItemDetail(OrderItemListDto dto, Order order, OrderLine line)
    {
        var item = order.Detail?.Items.FirstOrDefault(i =>
            !string.IsNullOrEmpty(i.RemoteLineId) && i.RemoteLineId == line.RemoteLineId);
        if (item is null)
        {
            return;
        }

        var hasDiscount = item.MallDiscount is not null || item.SellerDiscount is not null;
        dto.ItemDetail = new OrderItemDetailDto
        {
            SkuId = item.SkuId,
            Commission = item.Commission,
            DiscountTotal = hasDiscount ? (item.MallDiscount ?? 0m) + (item.SellerDiscount ?? 0m) : null,
            ShipmentCompany = item.ShipmentCompany,
            ShipmentMethod = item.ShipmentMethod,
            Attributes = FormatAttributes(item.Attributes),
        };
    }

    private static string? FormatAttributes(IReadOnlyList<OrderDetailItemAttribute> attributes)
    {
        if (attributes.Count == 0)
        {
            return null;
        }

        var text = string.Join(", ", attributes
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) || !string.IsNullOrWhiteSpace(a.Value))
            .Select(a => string.IsNullOrWhiteSpace(a.Name) ? a.Value : $"{a.Name}: {a.Value}"));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Sipariş (master) satırlarının kanal kodunu enrich eder (id-only referanstan; mapper doldurmaz).</summary>
    private async Task EnrichOrderChannelCodesAsync(Guid companyId, List<OrderListDto> dtos)
    {
        if (dtos.Count == 0)
        {
            return;
        }

        var codes = await GetChannelCodesAsync(companyId, dtos.Select(d => d.SalesChannelId).Distinct().ToList());
        foreach (var dto in dtos)
        {
            dto.SalesChannelCode = codes.TryGetValue(dto.SalesChannelId, out var code) ? code : null;
        }
    }

    /// <summary>Kanal kodu sözlüğü (id → Code) — sipariş listesinin ve karar bekleyenler sekmesinin ORTAK
    /// enrich kaynağı (aynı sorgu iki yerde kopyalanmasın; kanal referansı id-only, mapper doldurmaz).</summary>
    private async Task<Dictionary<Guid, string>> GetChannelCodesAsync(Guid companyId, List<Guid> channelIds)
    {
        if (channelIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return (await AsyncExecuter.ToListAsync(
                (await _channelRepository.GetQueryableAsync())
                    .Where(c => c.CompanyId == companyId && channelIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Code })))
            .ToDictionary(x => x.Id, x => x.Code);
    }

    private async Task<string?> ResolveChannelCodeAsync(Guid salesChannelId)
    {
        return await AsyncExecuter.FirstOrDefaultAsync(
            (await _channelRepository.GetQueryableAsync())
                .Where(c => c.Id == salesChannelId)
                .Select(c => c.Code));
    }

    private async Task<Order> GetOwnedOrderAsync(Guid id)
    {
        var companyId = EnsureCurrentCompanyId();
        var order = await AsyncExecuter.FirstOrDefaultAsync(
            (await _orderRepository.GetQueryableAsync()).Where(o => o.Id == id && o.CompanyId == companyId));
        if (order is null)
        {
            throw new BusinessException("TradeXpress:Order:NotFound");
        }

        return order;
    }

    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:Order:CompanyRequired");
        }

        return companyId;
    }

    /// <summary>Sipariş adres picker'ının TR ÖN-SEÇİM kataloğu (host-global; N11 TETİKLEMEZ). İthal il ADINDAN
    /// core idari-alanı (il) en-iyi-çaba eşler → picker İl'i ön-seçer. İlçe/mahalle KASITLI ön-seçilmez (canlı
    /// N11 mahalle fetch'inden kaçınmak için). Eşleşme yoksa <see cref="MatchArea"/> null döner (ithal ad korunur).</summary>
    private sealed class TurkeyAddressCatalog
    {
        public static readonly TurkeyAddressCatalog Empty = new(null, new List<AdministrativeAreaMatch>(), new List<LocalityMatch>());

        // İl adı → idari-alan (Trim + kültür-duyarsız). TR il adları benzersiz → ilk eşleşme kazanır.
        private readonly Dictionary<string, AdministrativeAreaMatch> _areaByName;

        // İl id → (ilçe adı → ilçe) (Trim + kültür-duyarsız). TR ilçe adları il içinde benzersiz.
        private readonly Dictionary<Guid, Dictionary<string, LocalityMatch>> _localitiesByArea;

        public TurkeyAddressCatalog(
            Guid? countryId,
            IReadOnlyCollection<AdministrativeAreaMatch> areas,
            IReadOnlyCollection<LocalityMatch> localities)
        {
            CountryId = countryId;
            _areaByName = new Dictionary<string, AdministrativeAreaMatch>(StringComparer.OrdinalIgnoreCase);
            foreach (var area in areas)
            {
                _areaByName.TryAdd(area.Name.Trim(), area);
            }

            _localitiesByArea = new Dictionary<Guid, Dictionary<string, LocalityMatch>>();
            foreach (var locality in localities)
            {
                if (!_localitiesByArea.TryGetValue(locality.AreaId, out var byName))
                {
                    byName = new Dictionary<string, LocalityMatch>(StringComparer.OrdinalIgnoreCase);
                    _localitiesByArea[locality.AreaId] = byName;
                }

                byName.TryAdd(locality.Name.Trim(), locality);
            }
        }

        /// <summary>TR ülke id'si (picker <c>FixedCountryId</c>'si) — TR kataloğu yoksa null.</summary>
        public Guid? CountryId { get; }

        /// <summary>İl adını core idari-alana eşler (Trim + kültür-duyarsız). Eşleşme yoksa null (ad korunur).</summary>
        public AdministrativeAreaMatch? MatchArea(string? cityName)
        {
            if (string.IsNullOrWhiteSpace(cityName))
            {
                return null;
            }

            return _areaByName.GetValueOrDefault(cityName.Trim());
        }

        /// <summary>İlçe adını, verilen il içinde core yerelliğe eşler (Trim + kültür-duyarsız). Yoksa null (ad korunur).</summary>
        public LocalityMatch? MatchLocality(Guid areaId, string? districtName)
        {
            if (string.IsNullOrWhiteSpace(districtName))
            {
                return null;
            }

            return _localitiesByArea.TryGetValue(areaId, out var byName)
                ? byName.GetValueOrDefault(districtName.Trim())
                : null;
        }
    }

    /// <summary>Core idari-alan (il) ön-seçim satırı — id + ad + kaynak kod + ISO 3166-2 kodu.</summary>
    private sealed record AdministrativeAreaMatch(Guid Id, string Name, string Code, string? IsoCode);

    /// <summary>Core yerellik (ilçe) ön-seçim satırı — id + il id + ad + kaynak (N11) ilçe kodu.</summary>
    private sealed record LocalityMatch(Guid Id, Guid AreaId, string Name, string Code);
}
