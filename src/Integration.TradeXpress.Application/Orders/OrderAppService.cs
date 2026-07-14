using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Trendyol;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// NÖTR sipariş uygulaması — ORTAK SİPARİŞ PANELİ (tüm kanallar tek grid, kanal yalnız discriminator) + pazaryerinden
/// SALT-OKUMA çekim (O0) + YEREL düzeltme katmanı (O1) + N11'e YAZAN state machine aksiyonları (O2 — kabul/red/kargo,
/// GERÇEK ve geri alınamaz). <b>Company-owned + per-tenant</b> (sunucu <see cref="ICurrentCompany"/> zorlar).
/// FİŞ YOK, REZERVASYON YOK, STOK HAREKETİ YOK. İdempotency anahtarı (SalesChannelId, RemoteOrderId): ikinci çekim
/// durumu/satırları günceller, dublike üretmez. Satırlar KENDİ tablosunda (id-only OrderId) — çekimde sil+yaz.
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class OrderAppService : TradeXpressAppService, IOrderAppService
{
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<OrderLine, Guid> _orderLineRepository;
    private readonly IRepository<OrderOperationalData, Guid> _operationalDataRepository;
    private readonly IRepository<OrderLineOperationalData, Guid> _operationalLineRepository;
    private readonly IRepository<ProductVariant, Guid> _productVariantRepository;
    private readonly IRepository<SalesChannelBase, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrN11, Guid> _n11ChannelRepository;
    private readonly OrderSyncManager _orderSyncManager;
    private readonly OrderLineProductSnapshotBuilder _snapshotBuilder;
    private readonly IN11OrderClient _n11OrderClient;
    private readonly ICurrentCompany _currentCompany;

    public OrderAppService(
        IRepository<Order, Guid> orderRepository,
        IRepository<OrderLine, Guid> orderLineRepository,
        IRepository<OrderOperationalData, Guid> operationalDataRepository,
        IRepository<OrderLineOperationalData, Guid> operationalLineRepository,
        IRepository<ProductVariant, Guid> productVariantRepository,
        IRepository<SalesChannelBase, Guid> channelRepository,
        IRepository<SalesChannelTrN11, Guid> n11ChannelRepository,
        OrderSyncManager orderSyncManager,
        OrderLineProductSnapshotBuilder snapshotBuilder,
        IN11OrderClient n11OrderClient,
        ICurrentCompany currentCompany)
    {
        _orderRepository = orderRepository;
        _orderLineRepository = orderLineRepository;
        _operationalDataRepository = operationalDataRepository;
        _operationalLineRepository = operationalLineRepository;
        _productVariantRepository = productVariantRepository;
        _channelRepository = channelRepository;
        _n11ChannelRepository = n11ChannelRepository;
        _n11OrderClient = n11OrderClient;
        _orderSyncManager = orderSyncManager;
        _snapshotBuilder = snapshotBuilder;
        _currentCompany = currentCompany;
    }

    // ── Ortak panel: birleşik liste (tüm kanallar) ────────────────────────────────────────────────────

    public virtual async Task<PagedResultDto<OrderListDto>> GetListAsync(OrderListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new PagedResultDto<OrderListDto>(0, new List<OrderListDto>());
        }

        // MASTER = SİPARİŞ (order düzeyinde sayfalı). Sıralama: order status → tarih (yeni→eski). DETAIL = siparişin
        // kalemleri (master-detail grid), master satır açılınca gösterilir.
        var orderQuery = (await _orderRepository.GetQueryableAsync())
            .Where(o => o.CompanyId == companyId)
            .OrderBy(o => o.NeutralStatus).ThenByDescending(o => o.OrderDate).ThenBy(o => o.Id);

        var totalCount = await AsyncExecuter.CountAsync(orderQuery);
        var orders = await AsyncExecuter.ToListAsync(orderQuery.Skip(input.SkipCount).Take(input.MaxResultCount));

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
        dto.BillingAddress = MapEditAddress(operational?.BillingAddressCorrection, order.Detail?.BillingAddress);
        dto.ShippingAddress = MapEditAddress(operational?.ShippingAddressCorrection, order.Detail?.ShippingAddress);
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

    private static OrderEditAddressDto MapEditAddress(OrderOperationalAddress? correction, OrderDetailAddress? original)
    {
        return new OrderEditAddressDto
        {
            FullName = correction?.FullName ?? original?.FullName,
            Line = correction?.Line ?? original?.Line,
            Neighborhood = correction?.Neighborhood ?? original?.Neighborhood,
            District = correction?.District ?? original?.District,
            City = correction?.City ?? original?.City,
            PostalCode = correction?.PostalCode ?? original?.PostalCode,
            Gsm = correction?.Gsm ?? original?.Gsm,
            TcId = correction?.TcId ?? original?.TcId,
            TaxId = correction?.TaxId ?? original?.TaxId,
            TaxOffice = correction?.TaxOffice ?? original?.TaxOffice,
        };
    }

    private static OrderOperationalAddress BuildOperationalAddress(OrderEditAddressDto dto)
    {
        return new OrderOperationalAddress(
            dto.FullName, dto.Line, dto.Neighborhood, dto.District, dto.City,
            dto.PostalCode, dto.Gsm, dto.TcId, dto.TaxId, dto.TaxOffice);
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

        var channelIds = dtos.Select(d => d.SalesChannelId).Distinct().ToList();
        var codes = (await AsyncExecuter.ToListAsync(
                (await _channelRepository.GetQueryableAsync())
                    .Where(c => c.CompanyId == companyId && channelIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Code })))
            .ToDictionary(x => x.Id, x => x.Code);

        foreach (var dto in dtos)
        {
            dto.SalesChannelCode = codes.TryGetValue(dto.SalesChannelId, out var code) ? code : null;
        }
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
}
