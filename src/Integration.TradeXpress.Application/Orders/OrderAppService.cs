using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Trendyol;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// NÖTR sipariş uygulaması — ORTAK SİPARİŞ PANELİ (tüm kanallar tek grid, kanal yalnız discriminator) + pazaryerinden
/// SALT-OKUMA çekim (O0). <b>Company-owned + per-tenant</b> (sunucu <see cref="ICurrentCompany"/> zorlar). FİŞ YOK,
/// REZERVASYON YOK, STOK HAREKETİ YOK, pazaryerine YAZMA YOK — yalnız GET + kendi tablomuza idempotent upsert +
/// görüntüleme. İdempotency anahtarı (SalesChannelId, RemoteOrderId): ikinci çekim durumu/satırları günceller,
/// dublike üretmez. Satırlar KENDİ tablosunda (id-only OrderId) — çekimde sil+yaz (snapshot, ürün-agnostik).
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class OrderAppService : TradeXpressAppService, IOrderAppService
{
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<OrderLine, Guid> _orderLineRepository;
    private readonly IRepository<SalesChannelBase, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _trendyolChannelRepository;
    private readonly IRepository<SalesChannelTrN11, Guid> _n11ChannelRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository;
    private readonly ITrendyolOrderClient _orderClient;
    private readonly IN11OrderClient _n11OrderClient;
    private readonly ICurrentCompany _currentCompany;

    // Grid whitelist — Order ENTITY property adları (ApplyListRequest bunlara karşı doğrular; global arama string
    // alanlarında OR-Contains). Sir/iç alanlar (RemoteOrderId) listede filtrelenmez.
    private static readonly HashSet<string> AllowedListFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id", "ChannelType", "OrderNumber", "OrderDate", "NeutralStatus",
        "RemoteStatus", "CustomerName", "TotalAmount", "CargoProvider", "CargoTrackingNumber", "FetchedAt",
    };

    /// <summary>Çekim geriye-bakış penceresi. Trendyol sipariş ucu YALNIZ son ~1 ayı servis eder (retention, resmî
    /// doküman) ve <c>PackageLastModifiedDate</c>'e göre filtreler → daha geriye gitmek boşuna (boş pencereler +
    /// gereksiz rate-limit baskısı). 1 ay + tampon; 14 günlük dilimlerle taranır. Tam tarihsel backfill AYRI iştir
    /// (<c>getShipmentPackagesStream</c> ucu). lastModified filtresi sayesinde durum değişen siparişler de yakalanır
    /// (idempotent upsert güncelleme yapar).</summary>
    private static readonly TimeSpan FetchLookback = TimeSpan.FromDays(40);

    public OrderAppService(
        IRepository<Order, Guid> orderRepository,
        IRepository<OrderLine, Guid> orderLineRepository,
        IRepository<SalesChannelBase, Guid> channelRepository,
        IRepository<SalesChannelTrTrendyol, Guid> trendyolChannelRepository,
        IRepository<SalesChannelTrN11, Guid> n11ChannelRepository,
        IRepository<CurrencyUnit, Guid> currencyUnitRepository,
        ITrendyolOrderClient orderClient,
        IN11OrderClient n11OrderClient,
        ICurrentCompany currentCompany)
    {
        _orderRepository = orderRepository;
        _orderLineRepository = orderLineRepository;
        _channelRepository = channelRepository;
        _trendyolChannelRepository = trendyolChannelRepository;
        _n11ChannelRepository = n11ChannelRepository;
        _currencyUnitRepository = currencyUnitRepository;
        _orderClient = orderClient;
        _n11OrderClient = n11OrderClient;
        _currentCompany = currentCompany;
    }

    // ── Ortak panel: birleşik liste (tüm kanallar) ────────────────────────────────────────────────────

    public virtual async Task<PagedResultDto<OrderListDto>> GetListAsync(OrderListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new PagedResultDto<OrderListDto>(0, new List<OrderListDto>());
        }

        var query = (await _orderRepository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId)
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

        var dtos = items
            .Select(e => ObjectMapper.Map<Order, OrderListDto>(e))
            .ToList();
        await EnrichChannelCodesAsync(companyId, dtos);

        return new PagedResultDto<OrderListDto>(totalCount, dtos);
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
        return dto;
    }

    // ── Çekim (salt GET → idempotent upsert) ──────────────────────────────────────────────────────────

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<OrderFetchResultDto> FetchOrdersAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var report = new OrderFetchResultDto();
        var tryCurrencyUnitId = await ResolveTryCurrencyUnitIdAsync(report);

        // Kanal tipini discriminator'dan çöz → doğru istemciyle çek (Trendyol REST / N11 SOAP), upsert ORTAK.
        var trendyol = await AsyncExecuter.FirstOrDefaultAsync(
            (await _trendyolChannelRepository.GetQueryableAsync())
                .Where(c => c.Id == salesChannelId && c.CompanyId == companyId));
        if (trendyol is not null)
        {
            await FetchTrendyolIntoAsync(trendyol, tryCurrencyUnitId, report);
        }
        else
        {
            var n11 = await AsyncExecuter.FirstOrDefaultAsync(
                (await _n11ChannelRepository.GetQueryableAsync())
                    .Where(c => c.Id == salesChannelId && c.CompanyId == companyId))
                ?? throw new BusinessException("TradeXpress:Order:ChannelNotFound");
            await FetchN11IntoAsync(n11, tryCurrencyUnitId, report);
        }

        report.ChannelsProcessed = 1;
        return report;
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<OrderFetchResultDto> FetchAllOrdersAsync()
    {
        var companyId = EnsureCurrentCompanyId();
        var report = new OrderFetchResultDto();
        var tryCurrencyUnitId = await ResolveTryCurrencyUnitIdAsync(report);

        var trendyolChannels = await AsyncExecuter.ToListAsync(
            (await _trendyolChannelRepository.GetQueryableAsync()).Where(c => c.CompanyId == companyId));
        foreach (var channel in trendyolChannels)
        {
            await FetchTrendyolIntoAsync(channel, tryCurrencyUnitId, report);
            report.ChannelsProcessed++;
        }

        var n11Channels = await AsyncExecuter.ToListAsync(
            (await _n11ChannelRepository.GetQueryableAsync()).Where(c => c.CompanyId == companyId));
        foreach (var channel in n11Channels)
        {
            await FetchN11IntoAsync(channel, tryCurrencyUnitId, report);
            report.ChannelsProcessed++;
        }

        if (trendyolChannels.Count == 0 && n11Channels.Count == 0)
        {
            report.Warnings.Add(L["Order:Fetch:NoChannel"].Value);
        }

        return report;
    }

    // ── Kanal-özel çekim (parse istemcide) → ORTAK upsert ──────────────────────────────────────────────

    private async Task FetchTrendyolIntoAsync(SalesChannelTrTrendyol channel, Guid? tryCurrencyUnitId, OrderFetchResultDto report)
    {
        var credentials = new TrendyolCredentials(channel.SellerId, channel.ApiKey, channel.ApiSecret);
        var remoteOrders = await _orderClient.GetAllOrdersAsync(credentials, RecordWindow(report));
        await UpsertOrdersAsync(channel.CompanyId, channel.Id, SalesChannelType.TrTrendyol,
            remoteOrders, TrendyolOrderStatusMapper.Map, tryCurrencyUnitId, report);
    }

    // N11 tarih filtresi göndermez → TÜM geçmiş gelir (N11 uzun retention). Bu yüzden N11'de "çekim penceresi" YOK
    // (report.FetchedSinceUtc yalnız Trendyol'un retention-sınırlı penceresini yansıtır).
    private async Task FetchN11IntoAsync(SalesChannelTrN11 channel, Guid? tryCurrencyUnitId, OrderFetchResultDto report)
    {
        var remoteOrders = await _n11OrderClient.GetAllOrdersAsync(channel.AppKey, channel.AppSecret);
        await UpsertOrdersAsync(channel.CompanyId, channel.Id, SalesChannelType.TrN11,
            remoteOrders, N11OrderStatusMapper.Map, tryCurrencyUnitId, report);
    }

    // Çekim geriye-bakış penceresini hesaplar + rapora yazar (en eski since; şeffaflık — sessiz kapsam düşürme yasak).
    private DateTime RecordWindow(OrderFetchResultDto report)
    {
        var sinceUtc = Clock.Now.ToUniversalTime() - FetchLookback;
        report.FetchedSinceUtc = report.FetchedSinceUtc is { } prev && prev < sinceUtc ? prev : sinceUtc;
        return sinceUtc;
    }

    /// <summary>KANAL-AGNOSTİK idempotent upsert ((SalesChannelId, RemoteOrderId)): mevcut sipariş bulunursa
    /// durumu/satırları güncellenir (dublike yok), yoksa yeni sipariş açılır. Satırlar sil+yaz (snapshot;
    /// ProductVariantId şimdilik null — O1 doldurur). Nötr durum <paramref name="statusMapper"/> ile (kanala göre).</summary>
    private async Task UpsertOrdersAsync(
        Guid companyId, Guid channelId, SalesChannelType channelType, IReadOnlyList<RemoteOrder> remoteOrders,
        Func<string?, OrderStatus> statusMapper, Guid? tryCurrencyUnitId, OrderFetchResultDto report)
    {
        // Kanalın mevcut siparişleri — RemoteOrderId anahtarıyla eşleşme (bellek-içi; çekim bağlamında yeterli).
        var existing = (await AsyncExecuter.ToListAsync(
                (await _orderRepository.GetQueryableAsync())
                    .Where(o => o.CompanyId == companyId && o.SalesChannelId == channelId)))
            .ToDictionary(o => o.RemoteOrderId, StringComparer.Ordinal);

        var fetchedAt = Clock.Now.ToUniversalTime();

        foreach (var remote in remoteOrders)
        {
            report.FetchedOrders++;

            if (string.IsNullOrWhiteSpace(remote.RemoteOrderId))
            {
                // Anahtarsız uzak kayıt idempotent upsert edilemez → sessizce ATLA değil, raporla.
                report.Warnings.Add(L["Order:Fetch:MissingRemoteId", remote.OrderNumber].Value);
                continue;
            }

            var neutralStatus = statusMapper(remote.RemoteStatus);

            if (existing.TryGetValue(remote.RemoteOrderId, out var order))
            {
                order.ApplyRemote(
                    remote.OrderNumber, remote.OrderDate, neutralStatus, remote.RemoteStatus, remote.CustomerName,
                    remote.TotalAmount, tryCurrencyUnitId, remote.CargoProvider, remote.CargoTrackingNumber, fetchedAt);
                await _orderRepository.UpdateAsync(order, autoSave: true);
                await ReplaceLinesAsync(companyId, order.Id, remote.Lines, report);
                report.UpdatedOrders++;
            }
            else
            {
                order = new Order(companyId, channelId, channelType, remote.RemoteOrderId, remote.OrderNumber);
                order.ApplyRemote(
                    remote.OrderNumber, remote.OrderDate, neutralStatus, remote.RemoteStatus, remote.CustomerName,
                    remote.TotalAmount, tryCurrencyUnitId, remote.CargoProvider, remote.CargoTrackingNumber, fetchedAt);
                await _orderRepository.InsertAsync(order, autoSave: true);
                existing[remote.RemoteOrderId] = order;
                await ReplaceLinesAsync(companyId, order.Id, remote.Lines, report);
                report.NewOrders++;
            }
        }
    }

    /// <summary>Satırları SİL+YAZ ile tazeler (snapshot; idempotent — ikinci çekim aynı sonucu üretir). Ürün adı
    /// boş gelirse barkod/stok koduna, o da yoksa "-" fallback'ine düşer (entity zorunlu alan guard'ını tetiklemeden;
    /// import onarım felsefesiyle aynı — kalem kaybetmek daha kötü).</summary>
    private async Task ReplaceLinesAsync(Guid companyId, Guid orderId, IReadOnlyList<RemoteOrderLine> lines, OrderFetchResultDto report)
    {
        await _orderLineRepository.DeleteAsync(l => l.OrderId == orderId, autoSave: true);

        foreach (var remoteLine in lines)
        {
            var name = FirstNonEmpty(remoteLine.ProductName, remoteLine.Barcode, remoteLine.StockCode) ?? "-";
            var snapshot = new OrderLineSnapshot(
                RemoteLineId: remoteLine.RemoteLineId,
                Barcode: remoteLine.Barcode,
                StockCode: remoteLine.StockCode,
                ProductNameSnapshot: name,
                Quantity: remoteLine.Quantity,
                UnitPrice: remoteLine.UnitPrice,
                LineTotal: remoteLine.LineTotal,
                RemoteLineStatus: remoteLine.RemoteLineStatus,
                ProductVariantId: null);   // O1 rezerve — snapshot esas, link opsiyonel
            await _orderLineRepository.InsertAsync(new OrderLine(companyId, orderId, snapshot), autoSave: true);
            report.TotalLines++;
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Liste satırlarının kanal kodunu enrich eder (id-only referanstan; mapper doldurmaz).</summary>
    private async Task EnrichChannelCodesAsync(Guid companyId, List<OrderListDto> dtos)
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

    /// <summary>TRY para birimi (Trendyol tutarı HER ZAMAN TRY) — HOST kaydından TENANT bağlamında çözülür
    /// (filtre-kapalı okuma; import ResolveTryCurrencyUnitIdAsync deseni). Bulunamazsa null + rapora uyarı.</summary>
    private async Task<Guid?> ResolveTryCurrencyUnitIdAsync(OrderFetchResultDto report)
    {
        using (DataFilter.Disable<Volo.Abp.MultiTenancy.IMultiTenant>())
        {
            var candidates = await AsyncExecuter.ToListAsync(
                (await _currencyUnitRepository.GetQueryableAsync()).Where(c => c.Code == CurrencyUnitCode.TRY));
            var preferred = candidates.FirstOrDefault(c => c.TenantId == CurrentTenant.Id)
                            ?? candidates.FirstOrDefault(c => c.TenantId == null);
            if (preferred is null)
            {
                report.Warnings.Add(L["Order:Fetch:TryCurrencyMissing"].Value);
            }

            return preferred?.Id;
        }
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
