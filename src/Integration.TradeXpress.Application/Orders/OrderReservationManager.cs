using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Orchestration;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Localization;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// SİPARİŞ REZERVASYONU ORKESTRASYONU (Faz 7) — sipariş çekildiği anda reçetedeki emtiayı müşteriye ayırır.
///
/// <para><b>KOŞULSUZ devreye girer</b> (2026-08-05 Hakan kararı #9): stok yetmese bile rezervasyon yazılır ve
/// <c>Available</c> EKSİYE düşer — <i>"hata yapmışsak cezasını biz çekeriz ki tutarlılık sürsün"</i>. Defter
/// dürüst kalır; kırpma KANAL sınırında yapılır (<c>SellableStockCalculator</c> zaten 0'a kırpıyor), hesapta
/// değil.</para>
///
/// <para><b>İDEMPOTENT:</b> sipariş senkron worker'ı aynı siparişi 2 dakikada bir yeniden upsert eder. Zaten
/// rezerve/karşılanmış siparişte hiçbir şey yapılmaz — aksi halde her turda yeni bir rezervasyon fişi doğar
/// ve stok kendi kendine tükenirdi.</para>
///
/// <para><b>Kurulamayan rezervasyon SESSİZ ATLANMAZ:</b> kayıt <c>Blocked</c> gerekçesiyle açılır ve gelen
/// kutusuna düşer. Eski davranış (eşleşmeyen kalemi sessizce geçmek) rezervasyon eklendikten sonra çok daha
/// tehlikeli olurdu: kullanıcı "rezerve edilmiş" sanırdı.</para>
/// </summary>
public class OrderReservationManager : ITransientDependency
{
    private const string ProductEntityName = "Product";

    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<OrderLine, Guid> _orderLineRepository;
    private readonly IRepository<OrderLineOperationalData, Guid> _operationalLineRepository;
    private readonly IRepository<OrderReservation, Guid> _reservationRepository;
    private readonly IRepository<OrderFulfillmentLink, Guid> _linkRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IRepository<Good, Guid> _goodRepository;
    private readonly IRepository<SalesChannelBase, Guid> _channelRepository;
    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly OrderReservationVoucherMaterializer _materializer;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly CommodityStockChangeQueuer _stockChangeQueuer;
    private readonly IClock _clock;
    private readonly IStringLocalizer<TradeXpressResource> _l;
    private readonly ILogger<OrderReservationManager> _logger;

    public OrderReservationManager(
        IRepository<Order, Guid> orderRepository,
        IRepository<OrderLine, Guid> orderLineRepository,
        IRepository<OrderLineOperationalData, Guid> operationalLineRepository,
        IRepository<OrderReservation, Guid> reservationRepository,
        IRepository<OrderFulfillmentLink, Guid> linkRepository,
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        IRepository<Metal, Guid> metalRepository,
        IRepository<Good, Guid> goodRepository,
        IRepository<SalesChannelBase, Guid> channelRepository,
        IRepository<Voucher, Guid> voucherRepository,
        OrderReservationVoucherMaterializer materializer,
        IAsyncQueryableExecuter asyncExecuter,
        IUnitOfWorkManager uowManager,
        CommodityStockChangeQueuer stockChangeQueuer,
        IClock clock,
        IStringLocalizer<TradeXpressResource> l,
        ILogger<OrderReservationManager> logger)
    {
        _orderRepository           = orderRepository;
        _orderLineRepository       = orderLineRepository;
        _operationalLineRepository = operationalLineRepository;
        _reservationRepository     = reservationRepository;
        _linkRepository            = linkRepository;
        _recipeLineRepository      = recipeLineRepository;
        _metalRepository           = metalRepository;
        _goodRepository            = goodRepository;
        _channelRepository         = channelRepository;
        _voucherRepository         = voucherRepository;
        _materializer              = materializer;
        _asyncExecuter             = asyncExecuter;
        _uowManager                = uowManager;
        _stockChangeQueuer         = stockChangeQueuer;
        _clock                     = clock;
        _l                         = l;
        _logger                    = logger;
    }

    /// <summary>Sipariş için rezervasyonu GARANTİ eder (idempotent). Zaten rezerve/karşılanmışsa hiçbir şey
    /// yapmaz. Kurulamayan rezervasyon <c>Blocked</c> gerekçesiyle kaydedilir.
    /// <para>TERMİNAL siparişte (teslim/iptal/iade) hiç kayıt açılmaz → dönüş <c>null</c>.</para></summary>
    public virtual async Task<OrderReservation?> EnsureReservationAsync(Guid companyId, Guid orderId)
    {
        var reservation = await _asyncExecuter.FirstOrDefaultAsync(
            (await _reservationRepository.GetQueryableAsync()).Where(r => r.OrderId == orderId));

        if (reservation is { Status: OrderReservationStatus.Reserved or OrderReservationStatus.Fulfilled })
        {
            return reservation;   // İDEMPOTENT: worker her 2 dakikada bir aynı siparişle geri geliyor.
        }

        // Serbest bırakılmış rezervasyon YENİDEN kurulmaz: iptal onaylanıp stok geri verildikten sonra
        // senkron döngüsünün onu kendiliğinden diriltmesi, kullanıcının kararını sessizce geri alırdı.
        if (reservation is { Status: OrderReservationStatus.Released })
        {
            return reservation;
        }

        var order = await _orderRepository.GetAsync(orderId);

        // ⚠ TERMİNAL KAPISI: teslim edilmiş / iptal edilmiş / iade edilmiş sipariş rezervasyon KURMAZ.
        // Kapı burada, çağıranda değil: rezervasyonun ne zaman meşru olduğu bu sınıfın bilgisidir ve
        // ileride başka bir çağıran eklenirse kuralı yeniden yazmak gerekmez.
        // Kayıt AÇILMAZ (2026-08-07 Hakan kararı): terminal sipariş için "bloklandı" ya da "serbest bırakıldı"
        // demek yanlış olurdu — ortada hiç kurulmamış bir taahhüt var, geri alınan bir şey yok. Gelen kutusunu
        // 106 tarihsel siparişle doldurmak da gerçek işi görünmez kılardı.
        // Mevcut kayda DOKUNULMAZ: sipariş rezerve edildikten SONRA teslime geçmişse fiziki çıkışa çevirmek
        // state machine'in işidir; burada silmek denetim izini yok ederdi.
        if (order.NeutralStatus.IsTerminal())
        {
            return reservation;
        }

        reservation ??= new OrderReservation(companyId, orderId);

        try
        {
            // ⚠ FİŞ + BAĞLAR + MarkReserved TEK ATOMİK BİRİMDİR. Fiş InsertNumberedAsync'te autoSave:true ile
            // yazılır; dış bağlamda açık transaction OLMADIĞI için ANINDA commit ediliyordu. Sonraki adım
            // (bağ insert'i ya da MarkReserved) patlayınca catch yalnız Blocked yazıyor, fişe dokunmuyordu →
            // geriye SAHİPSİZ bir rezervasyon fişi kalıyor ve ReservedOut kalıcı olarak şişiyordu. Üstelik
            // rezervasyon Blocked olduğu için bir sonraki tur yeniden deniyor ve İKİNCİ fişi açıyordu.
            // Kendi transactional UoW'u: istisnada hepsi birlikte geri sarılır.
            using (var uow = _uowManager.Begin(requiresNew: true, isTransactional: true))
            {
                var lines = await BuildLinesAsync(companyId, orderId);
                if (lines.Count == 0)
                {
                    await BlockAsync(reservation,
                        "Sipariş kalemleri yerel ürün varyantına eşleşmedi ya da reçetesi yok.");
                    await uow.CompleteAsync();
                    return reservation;
                }

                var channel = await _channelRepository.FindAsync(order.SalesChannelId);

                var voucher = await _materializer.MaterializeAsync(
                    companyId,
                    channel?.SubAccountId,
                    lines.ConvertAll(l => l.Line),
                    $"Sipariş rezervasyonu: {order.OrderNumber}");

                await UpsertAsync(reservation);   // Id gerekiyor: bağ kayıtları rezervasyondan SONRA yazılır.

                // Kalem ↔ fiş satırı bağları — ÇOKA-ÇOK tablosu (birleştirme senaryosu bunun üzerine kurulacak).
                for (var i = 0; i < lines.Count; i++)
                {
                    var link = new OrderFulfillmentLink(
                        companyId, orderId, lines[i].RemoteLineId,
                        voucher.VoucherId, voucher.LineIds[i], OrderFulfillmentLinkKind.Reservation);
                    link.SetFulfilled(lines[i].Line.Quantity, lines[i].Line.Amount);
                    await _linkRepository.InsertAsync(link, autoSave: true);
                }

                reservation.MarkReserved(voucher.VoucherId, _clock.Now.ToUniversalTime());
                await _reservationRepository.UpdateAsync(reservation, autoSave: true);

                await uow.CompleteAsync();
                return reservation;
            }
        }
        catch (Exception ex)
        {
            // Rezervasyon kurulamadı → sipariş senkronu DÜŞMEZ (bir siparişin sorunu diğerlerini engellemez),
            // ama SESSİZ de geçilmez: gerekçe kayda ve loga yazılır.
            _logger.LogWarning(ex, "Sipariş rezervasyonu kurulamadı: Order={OrderId}.", orderId);
            return await BlockAfterRollbackAsync(companyId, orderId, DescribeFailure(ex));
        }
    }

    /// <summary>Kullanıcıya GÖSTERİLECEK gerekçe metni.
    ///
    /// <para><b>Neden ham <c>ex.Message</c> yetmiyor:</b> kodlu <c>BusinessException</c>'ların mesajı
    /// çözülmediğinde gelen kutusuna "TradeXpress:OrderReservation:ChannelSubAccountMissing" gibi bir anahtar
    /// düşüyordu — kullanıcı için hiçbir anlam taşımayan, üstelik hatanın ne olduğunu değil NEREDE tanımlandığını
    /// söyleyen bir metin.</para>
    ///
    /// <para><b>Kültür SABİTLENİR (tr):</b> worker'ın kültürü belirsizdir ve <c>BlockAsync</c> gerekçeyi
    /// ORDINAL karşılaştırır. Kültür turlar arasında değişseydi aynı hata her turda "değişmiş" görünür ve
    /// susturulması gereken tekrarlı yazım geri gelirdi.</para></summary>
    private string DescribeFailure(Exception ex)
    {
        if (ex is BusinessException { Code: { Length: > 0 } code })
        {
            using (CultureHelper.Use("tr"))
            {
                var localized = _l[code];
                if (!localized.ResourceNotFound)
                {
                    return localized.Value;
                }
            }
        }

        return ex.Message;
    }

    /// <summary><c>Blocked</c>'ı DIŞ UoW'da, veritabanından TAZE okunan kayıtla yazar.
    ///
    /// <para><b>Neden taze okuma:</b> iç transaction geri sarıldığı için elimizdeki nesne artık yalan söylüyor —
    /// <c>UpsertAsync</c> insert/update kararını <c>Id</c>'ye bakarak verir ve rollback'ten önce insert edilmiş
    /// nesnenin <c>Id</c>'si DOLU kalır. O nesneyle devam etseydik var olmayan satıra UPDATE atılır ve
    /// gerekçe hiç yazılamazdı: rezervasyon kurulamadığı hâlde <b>hiçbir iz bırakmadan</b> kaybolurdu.</para></summary>
    private async Task<OrderReservation> BlockAfterRollbackAsync(Guid companyId, Guid orderId, string reason)
    {
        var reservation = await _asyncExecuter.FirstOrDefaultAsync(
            (await _reservationRepository.GetQueryableAsync()).Where(r => r.OrderId == orderId));

        reservation ??= new OrderReservation(companyId, orderId);
        await BlockAsync(reservation, reason);
        return reservation;
    }

    /// <summary>Rezervasyonu <c>Blocked</c> olarak kaydeder — <b>DURUM DEĞİŞMEDİYSE YAZMAZ</b>.
    ///
    /// <para>Sipariş senkron worker'ı 2 dakikada bir tüm siparişleri geçer. Eşleşmesi olmayan sipariş her
    /// turda aynı gerekçeyle yeniden bloklanıyordu ve bu, hiçbir şey değişmediği hâlde her turda bir UPDATE
    /// üretiyordu: canlıda eşleşmemiş 106 sipariş var, yani günde ~76 bin gereksiz yazım + denetim gürültüsü
    /// (2026-08-06 ölçümü).</para>
    ///
    /// <para><b>Yeniden DENEME sürüyor</b> (yalnız YAZIM susturuldu): operatör kalemi elle eşleştirdiğinde
    /// bir sonraki tur rezervasyonu kurabilmeli. Denemenin maliyeti iki SELECT'tir; asıl israf yazımdaydı.</para></summary>
    private async Task BlockAsync(OrderReservation reservation, string reason)
    {
        var unchanged = reservation.Id != Guid.Empty
                        && reservation.Status == OrderReservationStatus.Blocked
                        && string.Equals(reservation.Note, reason, StringComparison.Ordinal);
        if (unchanged)
        {
            return;
        }

        reservation.MarkBlocked(reason);
        await UpsertAsync(reservation);
    }

    /// <summary>KANALDAN İPTAL TALEBİ geldi → rezervasyonun <b>karar ekseni</b> "bekliyor"a düşer.
    ///
    /// <para><b>STOK EKSENİNE DOKUNULMAZ</b> (§6 iki-eksen kuralı): maden tutulmaya devam eder. Kanal iptal
    /// dediği için stoğu kendiliğinden geri vermek, mal fiziksel olarak hazırlanmış/kesilmiş/eritilmiş olabilecekken
    /// defteri yalanlamak olurdu — bunu yalnız kullanıcı bilir. Hiçbir iptal otomatik değildir.</para>
    ///
    /// <para><b>Serbest bırakılmış rezervasyon atlanır</b> — zaten stok geri verilmiş, karar beklenecek bir şey
    /// yok. <c>Blocked</c> ve <c>Fulfilled</c> ATLANMAZ: ikisinde de kullanıcının görmesi gereken bir talep var
    /// (Fulfilled'da onay entity guard'ına çarpar, reddi kullanıcı verir).</para></summary>
    public virtual async Task<OrderReservation?> NotifyCancellationRequestedAsync(Guid orderId)
    {
        var reservation = await _asyncExecuter.FirstOrDefaultAsync(
            (await _reservationRepository.GetQueryableAsync()).Where(r => r.OrderId == orderId));

        if (reservation is null || reservation.Status == OrderReservationStatus.Released)
        {
            return reservation;
        }

        var before = reservation.CancellationDecision;
        reservation.RequestCancellation(_clock.Now.ToUniversalTime());

        // DEĞİŞMEDİYSE YAZMA (BlockAsync ile aynı disiplin): worker 2 dakikada bir aynı siparişle döner;
        // koşulsuz UPDATE denetim izini gürültüye boğar ve hiçbir şey eklemez.
        if (reservation.CancellationDecision == before)
        {
            return reservation;
        }

        await _reservationRepository.UpdateAsync(reservation, autoSave: true);
        return reservation;
    }

    /// <summary>Rezervasyonu SERBEST BIRAKIR: fiş satırları soft-delete edilir (sayaçtan düşer, denetim izi
    /// kalır — <c>Voucher.RemoveLine</c> koleksiyondan çıkarmaz, DB'de <c>IsDeleted</c> bırakır ve rapor
    /// <c>!IsDeleted</c> filtreler), rezervasyon <c>Released</c> olur.
    /// <para>Karşılanmış (<c>Fulfilled</c>) rezervasyon serbest BIRAKILAMAZ — entity guard'ı bloklar.</para></summary>
    public virtual async Task<OrderReservation?> ReleaseAsync(Guid orderId, string? reason = null)
    {
        var reservation = await _asyncExecuter.FirstOrDefaultAsync(
            (await _reservationRepository.GetQueryableAsync()).Where(r => r.OrderId == orderId));
        if (reservation is null || reservation.Status != OrderReservationStatus.Reserved)
        {
            return reservation;
        }

        if (reservation.VoucherId is { } voucherId)
        {
            var voucher = await _voucherRepository.FindAsync(voucherId);
            if (voucher is not null)
            {
                await _voucherRepository.EnsureCollectionLoadedAsync(voucher, v => v.Lines);

                // ⚠ ANAHTARLAR SİLMEDEN ÖNCE toplanır: soft-delete'ten sonra satırlar `IsDeleted` olur ve
                // toplayıcı onları ELER — "sonraki duruma" bakan bir tetik BOŞ küme görür, yani hiçbir emtia
                // için olay yayımlanmaz. Serbest bırakılan madenin kanal stoğu bir sonraki tam turu beklerdi.
                var beforeKeys = CommodityStockChangeQueuer.CollectKeys(voucher);

                foreach (var line in voucher.Lines.Where(l => !l.IsDeleted).ToList())
                {
                    voucher.RemoveLine(line.Id);
                }

                await _voucherRepository.UpdateAsync(voucher, autoSave: true);

                // STOK TETİĞİ (E-9): rezervasyonun düşmesi `AvailableAmount`'ı ARTIRIR — zincir
                // (ters-endeks → pusher) boşalan stoğu kanala kendiliğinden taşır. Bu tetik eskiden HİÇ YOKTU:
                // hata üretmiyordu, yalnız stok pazaryerinde 15 dakikaya kadar eksik görünüyordu.
                _stockChangeQueuer.QueueForVoucher(voucher, beforeKeys);
            }
        }

        reservation.MarkReleased(_clock.Now.ToUniversalTime(), reason);
        await _reservationRepository.UpdateAsync(reservation, autoSave: true);
        return reservation;
    }

    // ────────────────────────────────────────────────────────────────────────────────
    //  Reçete → rezervasyon satırı
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Siparişin eşleşmiş kalemlerinden rezervasyon satırlarını üretir:
    /// <c>kalem adedi × reçete satırı</c>. Yalnız stok-taşıyan aileler (<see cref="CommodityStockFamilies"/>).
    /// <para><b>⚠ Birimi olmayan satır ATLANMAZ, İSTİSNA fırlatır:</b> stok raporu <c>MainUnitId</c>'siz satırı
    /// sessizce eler; böyle bir satır yazılsaydı "rezerve edildi" görünür ama stok hiç düşmezdi.</para></summary>
    private async Task<List<ReservationLineDraft>> BuildLinesAsync(Guid companyId, Guid orderId)
    {
        var matches = await _asyncExecuter.ToListAsync(
            (await _operationalLineRepository.GetQueryableAsync())
                .Where(o => o.OrderId == orderId && o.ProductVariantId != null)
                .Select(o => new { o.RemoteLineId, VariantId = o.ProductVariantId!.Value }));
        if (matches.Count == 0)
        {
            return new List<ReservationLineDraft>();
        }

        var quantities = (await _asyncExecuter.ToListAsync(
                (await _orderLineRepository.GetQueryableAsync())
                    .Where(l => l.OrderId == orderId && l.RemoteLineId != null)
                    .Select(l => new { l.RemoteLineId, l.Quantity })))
            .GroupBy(l => l.RemoteLineId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity), StringComparer.OrdinalIgnoreCase);

        var variantIds = matches.Select(m => m.VariantId).Distinct().ToList();
        var recipeLines = await _asyncExecuter.ToListAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(l => variantIds.Contains(l.ProductVariantId)
                            && l.ComponentType == RecipeComponentType.CatalogCommodity
                            && l.CommodityProcessType != null
                            && CommodityStockFamilies.Tracked.Contains(l.CommodityProcessType!.Value)
                            && l.CommodityId != null));

        var codes = await ResolveCommodityCodesAsync(recipeLines);
        var byVariant = recipeLines.GroupBy(l => l.ProductVariantId).ToDictionary(g => g.Key, g => g.ToList());

        var drafts = new List<ReservationLineDraft>();
        foreach (var match in matches)
        {
            if (!byVariant.TryGetValue(match.VariantId, out var lines))
            {
                continue;   // varyantın reçetesi yok — rezerve edilecek bir şey yok
            }

            var orderedQuantity = quantities.GetValueOrDefault(match.RemoteLineId, 1m);
            if (orderedQuantity <= 0m)
            {
                orderedQuantity = 1m;
            }

            foreach (var line in lines)
            {
                if (line.ValuationUnitId is not { } unitId || unitId == Guid.Empty)
                {
                    throw new Volo.Abp.BusinessException("TradeXpress:OrderReservation:RecipeLineUnitMissing")
                        .WithData("CommodityId", line.CommodityId);
                }

                var family = line.CommodityProcessType!.Value;
                var commodityId = line.CommodityId!.Value;

                drafts.Add(new ReservationLineDraft(
                    match.RemoteLineId,
                    new OrderReservationLine(
                        family,
                        commodityId,
                        codes.GetValueOrDefault((family, commodityId)) ?? string.Empty,
                        line.CommodityVariantId,
                        line.Quantity * orderedQuantity,
                        line.Amount * orderedQuantity,
                        line.Factor == 0m ? 1m : line.Factor,
                        unitId,
                        null)));
            }
        }

        return drafts;
    }

    /// <summary>Emtia kodu snapshot'ı — fiş satırı kodu TAŞIR (rapor gruplaması ve kullanıcı görünümü onunla).
    /// Kod çözülemezse boş geçilir: kod eksikliği rezervasyonu iptal ettirecek kadar kritik değildir, id bağı
    /// zaten sağlamdır.</summary>
    private async Task<Dictionary<(ProcessType Family, Guid Id), string>> ResolveCommodityCodesAsync(
        List<ProductVariantRecipeLine> lines)
    {
        var result = new Dictionary<(ProcessType, Guid), string>();

        var metalIds = lines.Where(l => l.CommodityProcessType == ProcessType.Metal)
            .Select(l => l.CommodityId!.Value).Distinct().ToList();
        if (metalIds.Count > 0)
        {
            var rows = await _asyncExecuter.ToListAsync(
                (await _metalRepository.GetQueryableAsync())
                    .Where(m => metalIds.Contains(m.Id)).Select(m => new { m.Id, m.Code }));
            foreach (var row in rows)
            {
                result[(ProcessType.Metal, row.Id)] = row.Code;
            }
        }

        var goodIds = lines.Where(l => l.CommodityProcessType == ProcessType.Good)
            .Select(l => l.CommodityId!.Value).Distinct().ToList();
        if (goodIds.Count > 0)
        {
            var rows = await _asyncExecuter.ToListAsync(
                (await _goodRepository.GetQueryableAsync())
                    .Where(g => goodIds.Contains(g.Id)).Select(g => new { g.Id, g.Code }));
            foreach (var row in rows)
            {
                result[(ProcessType.Good, row.Id)] = row.Code;
            }
        }

        return result;
    }

    private async Task UpsertAsync(OrderReservation reservation)
    {
        if (reservation.Id == Guid.Empty)
        {
            await _reservationRepository.InsertAsync(reservation, autoSave: true);
            return;
        }

        await _reservationRepository.UpdateAsync(reservation, autoSave: true);
    }

    /// <summary>Rezervasyon satırı + hangi sipariş kalemine ait olduğu (bağ kaydı için).</summary>
    private sealed record ReservationLineDraft(string RemoteLineId, OrderReservationLine Line);
}
