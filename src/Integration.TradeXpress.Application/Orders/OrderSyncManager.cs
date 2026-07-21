using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Trendyol;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Sipariş SENKRONİZASYON çekirdeği — pazaryerinden STREAMING (sayfa-sayfa) çekip <b>order başına kaydeder</b> (fetch
/// bitmeden fresh veri akar; kısmi başarı korunur). Auth YOK → hem <see cref="OrderAppService"/> (manuel düğme, current
/// company) hem <see cref="OrderSyncBackgroundWorker"/> (tüm tenant/kanal, boş olanı seed'ler) buradan tüketir.
/// SALT-OKUMA çekim → idempotent upsert ((SalesChannelId, RemoteOrderId)); fiş/rezervasyon/stok'a HİÇ dokunmaz.
/// Worker bağlamında tenant kanaldan gelir (<see cref="CurrentTenant"/>.Change(channel.TenantId)).
/// </summary>
public class OrderSyncManager : DomainService
{
    private const int MaxPageLoops = 500;   // streaming sayfa güvenlik tavanı (bozuk pageCount → sonsuz döngü olmasın)

    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<OrderLine, Guid> _orderLineRepository;
    private readonly IRepository<SalesChannelTrN11, Guid> _n11ChannelRepository;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _trendyolChannelRepository;
    private readonly IRepository<SalesChannelEtsy, Guid> _etsyChannelRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository;
    private readonly IRepository<Tenant, Guid> _tenantRepository;
    private readonly IN11OrderClient _n11OrderClient;
    private readonly ITrendyolOrderClient _trendyolOrderClient;
    private readonly IEtsyOrderClient _etsyOrderClient;
    private readonly OrderLineProductMatcher _productMatcher;
    private readonly IDataFilter _dataFilter;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly IStringLocalizer<TradeXpressResource> _l;

    /// <summary>Trendyol geriye-bakış: yalnız son ~1 ayı servis eder (retention) → daha geriye gitmek boşuna.</summary>
    private static readonly TimeSpan TrendyolFetchLookback = TimeSpan.FromDays(40);

    public OrderSyncManager(
        IRepository<Order, Guid> orderRepository,
        IRepository<OrderLine, Guid> orderLineRepository,
        IRepository<SalesChannelTrN11, Guid> n11ChannelRepository,
        IRepository<SalesChannelTrTrendyol, Guid> trendyolChannelRepository,
        IRepository<SalesChannelEtsy, Guid> etsyChannelRepository,
        IRepository<CurrencyUnit, Guid> currencyUnitRepository,
        IRepository<Tenant, Guid> tenantRepository,
        IN11OrderClient n11OrderClient,
        ITrendyolOrderClient trendyolOrderClient,
        IEtsyOrderClient etsyOrderClient,
        OrderLineProductMatcher productMatcher,
        IDataFilter dataFilter,
        IUnitOfWorkManager uowManager,
        IStringLocalizer<TradeXpressResource> l)
    {
        _orderRepository = orderRepository;
        _orderLineRepository = orderLineRepository;
        _n11ChannelRepository = n11ChannelRepository;
        _trendyolChannelRepository = trendyolChannelRepository;
        _etsyChannelRepository = etsyChannelRepository;
        _currencyUnitRepository = currencyUnitRepository;
        _tenantRepository = tenantRepository;
        _n11OrderClient = n11OrderClient;
        _trendyolOrderClient = trendyolOrderClient;
        _etsyOrderClient = etsyOrderClient;
        _productMatcher = productMatcher;
        _dataFilter = dataFilter;
        _uowManager = uowManager;
        _l = l;
    }

    // ── Worker girişi: TÜM tenant'ların BOŞ kanallarını seed'le (streaming) ────────────────────────────

    /// <summary>Tüm tenant'lardaki her N11/Trendyol kanalı için: kanalın HİÇ siparişi yoksa TÜM geçmişi streaming
    /// çeker (period doldurmadan). Tenant kanaldan gelir → her kanal kendi tenant scope'unda işlenir. Kanal başına
    /// bağımsız try/catch (biri düşse — kimlik/ağ/throttle — diğerleri çalışır, worker çökmez).</summary>
    public virtual async Task<OrderFetchResultDto> SyncEmptyChannelsAsync(CancellationToken cancellationToken = default)
    {
        var report = new OrderFetchResultDto();

        // Tenant listesini kendi UoW'unda oku (host scope).
        List<Guid?> tenantIds;
        using (var uow = _uowManager.Begin(requiresNew: true))
        {
            tenantIds = (await AsyncExecuter.ToListAsync(await _tenantRepository.GetQueryableAsync()))
                .Select(t => (Guid?)t.Id)
                .ToList();
            await uow.CompleteAsync();
        }
        tenantIds.Add(null);   // host (TenantId=null) kanalları da dahil

        // Tüm tenant'ları (+ host) TEK TEK dolaş. KRİTİK: Change SONRASI TAZE requiresNew UoW → DbContext o tenant'a
        // bağlanır, kanallar filtreyle DOĞAL görünür (dış UoW host'a bağlı kalıp kanalları gizliyordu).
        foreach (var tenantId in tenantIds)
        {
            // Worker ŞİRKETLER-ARASI (tenant'lar-arası gibi): CurrentCompany YOK → ICompanyScoped filtresi (CurrentCompanyId
            // set olduğunda) tenant kanallarını eler → 0 kanal → seed olmaz. Company filtresini DISABLE et: her tenant'ın
            // TÜM şirket kanalları görünür (tenant izolasyonu Change ile korunur). Seed edilen order kanalın CompanyId'siyle yazılır.
            using (CurrentTenant.Change(tenantId))
            using (_dataFilter.Disable<ICompanyScoped>())
            using (var uow = _uowManager.Begin(requiresNew: true))
            {
                var n11Channels = await AsyncExecuter.ToListAsync(await _n11ChannelRepository.GetQueryableAsync());
                var trendyolChannels = await AsyncExecuter.ToListAsync(await _trendyolChannelRepository.GetQueryableAsync());
                var etsyChannels = await AsyncExecuter.ToListAsync(await _etsyChannelRepository.GetQueryableAsync());

                if (n11Channels.Count > 0 || trendyolChannels.Count > 0 || etsyChannels.Count > 0)
                {
                    Logger.LogInformation("Sipariş seed: tenant {Tenant} → {N11} N11 + {Trendyol} Trendyol + {Etsy} Etsy kanal.",
                        tenantId, n11Channels.Count, trendyolChannels.Count, etsyChannels.Count);
                }

                foreach (var channel in n11Channels)
                {
                    await SeedChannelIfEmptyAsync(channel.Id, channel.CompanyId,
                        () => StreamN11ChannelAsync(channel, report, cancellationToken), "N11");
                }

                foreach (var channel in trendyolChannels)
                {
                    await SeedChannelIfEmptyAsync(channel.Id, channel.CompanyId,
                        () => StreamTrendyolChannelAsync(channel, report, cancellationToken), "Trendyol");
                }

                foreach (var channel in etsyChannels)
                {
                    await SeedChannelIfEmptyAsync(channel.Id, channel.CompanyId,
                        () => StreamEtsyChannelAsync(channel, report, cancellationToken), "Etsy");
                }

                await uow.CompleteAsync();
            }
        }

        return report;
    }

    // Kanal boşsa (hiç sipariş yok) streaming seed'ler; doluysa ucuza atlar. Kanal başına bağımsız try/catch.
    private async Task SeedChannelIfEmptyAsync(Guid channelId, Guid companyId, Func<Task> stream, string channelKind)
    {
        if (await ChannelHasOrdersAsync(companyId, channelId))
        {
            return;
        }

        Logger.LogInformation("Sipariş seed: {Kind} kanal {ChannelId} BOŞ → streaming başlıyor.", channelKind, channelId);
        await RunSafeAsync(stream, channelId);
    }

    // ── Manuel giriş (current company, tenant scope zaten var): kanalları çek (streaming) ───────────────

    /// <summary>Bir şirketin TÜM kanallarını çeker (manuel düğme). <paramref name="onlyEmpty"/>=false → hepsini
    /// yeniden çeker (idempotent). Rapor birleştirilir.</summary>
    public virtual async Task SyncCompanyAsync(Guid companyId, bool onlyEmpty, OrderFetchResultDto report, CancellationToken cancellationToken = default)
    {
        var n11Channels = await AsyncExecuter.ToListAsync(
            (await _n11ChannelRepository.GetQueryableAsync()).Where(c => c.CompanyId == companyId));
        foreach (var channel in n11Channels)
        {
            if (onlyEmpty && await ChannelHasOrdersAsync(companyId, channel.Id))
            {
                continue;
            }

            await StreamN11ChannelAsync(channel, report, cancellationToken);
        }

        var trendyolChannels = await AsyncExecuter.ToListAsync(
            (await _trendyolChannelRepository.GetQueryableAsync()).Where(c => c.CompanyId == companyId));
        foreach (var channel in trendyolChannels)
        {
            if (onlyEmpty && await ChannelHasOrdersAsync(companyId, channel.Id))
            {
                continue;
            }

            await StreamTrendyolChannelAsync(channel, report, cancellationToken);
        }

        var etsyChannels = await AsyncExecuter.ToListAsync(
            (await _etsyChannelRepository.GetQueryableAsync()).Where(c => c.CompanyId == companyId));
        foreach (var channel in etsyChannels)
        {
            if (onlyEmpty && await ChannelHasOrdersAsync(companyId, channel.Id))
            {
                continue;
            }

            await StreamEtsyChannelAsync(channel, report, cancellationToken);
        }

        if (n11Channels.Count == 0 && trendyolChannels.Count == 0 && etsyChannels.Count == 0)
        {
            report.Warnings.Add(_l["Order:Fetch:NoChannel"]);
        }
    }

    /// <summary>Tek kanalı çeker (manuel düğme, kanal id ile). Tip discriminator'dan çözülür. Kanal bu şirkete ait
    /// değilse dostane hata.</summary>
    public virtual async Task SyncSingleChannelAsync(Guid companyId, Guid channelId, OrderFetchResultDto report, CancellationToken cancellationToken = default)
    {
        var trendyol = await AsyncExecuter.FirstOrDefaultAsync(
            (await _trendyolChannelRepository.GetQueryableAsync()).Where(c => c.Id == channelId && c.CompanyId == companyId));
        if (trendyol is not null)
        {
            await StreamTrendyolChannelAsync(trendyol, report, cancellationToken);
            return;
        }

        var etsy = await AsyncExecuter.FirstOrDefaultAsync(
            (await _etsyChannelRepository.GetQueryableAsync()).Where(c => c.Id == channelId && c.CompanyId == companyId));
        if (etsy is not null)
        {
            await StreamEtsyChannelAsync(etsy, report, cancellationToken);
            return;
        }

        var n11 = await AsyncExecuter.FirstOrDefaultAsync(
            (await _n11ChannelRepository.GetQueryableAsync()).Where(c => c.Id == channelId && c.CompanyId == companyId))
            ?? throw new Volo.Abp.BusinessException("TradeXpress:Order:ChannelNotFound");
        await StreamN11ChannelAsync(n11, report, cancellationToken);
    }

    // ── Kanal-özel streaming (parse istemcide) → order başına save ──────────────────────────────────────

    private async Task StreamN11ChannelAsync(SalesChannelTrN11 channel, OrderFetchResultDto report, CancellationToken cancellationToken)
    {
        var tryCurrencyUnitId = await ResolveTryCurrencyUnitIdAsync(report);

        var page = 0;
        int pageCount;
        do
        {
            var result = await _n11OrderClient.GetOrdersPageAsync(channel.AppKey, channel.AppSecret, page, cancellationToken);
            pageCount = result.PageCount;
            Logger.LogInformation("Sipariş seed: N11 sayfa {Page} → {Count} sipariş (pageCount {PageCount}).", page, result.Orders.Count, pageCount);
            foreach (var remote in result.Orders)
            {
                // ZENGİN detay (getOrderDetail) order-başına çekilir → snapshot DB'ye saklanır (popup DB'den okur).
                var detail = await FetchN11DetailSafeAsync(channel, remote.RemoteOrderId, cancellationToken);
                await UpsertOrderAsync(channel.CompanyId, channel.Id, SalesChannelType.TrN11, remote,
                    N11OrderStatusMapper.Map, tryCurrencyUnitId, report, detail);
            }

            page++;
        }
        while (page < pageCount && page < MaxPageLoops);

        report.ChannelsProcessed++;
    }

    private async Task StreamTrendyolChannelAsync(SalesChannelTrTrendyol channel, OrderFetchResultDto report, CancellationToken cancellationToken)
    {
        var tryCurrencyUnitId = await ResolveTryCurrencyUnitIdAsync(report);

        // Trendyol retention ~1 ay → tek pencere yeterli; client 14 günlük dilim + sayfa döngüsünü içeride yapar.
        var sinceUtc = Clock.Now.ToUniversalTime() - TrendyolFetchLookback;
        report.FetchedSinceUtc = report.FetchedSinceUtc is { } prev && prev < sinceUtc ? prev : sinceUtc;

        var credentials = new TrendyolCredentials(channel.SellerId, channel.ApiKey, channel.ApiSecret);
        var remoteOrders = await _trendyolOrderClient.GetAllOrdersAsync(credentials, sinceUtc, cancellationToken: cancellationToken);
        foreach (var remote in remoteOrders)
        {
            await UpsertOrderAsync(channel.CompanyId, channel.Id, SalesChannelType.TrTrendyol, remote,
                TrendyolOrderStatusMapper.Map, tryCurrencyUnitId, report);
        }

        report.ChannelsProcessed++;
    }

    private async Task StreamEtsyChannelAsync(SalesChannelEtsy channel, OrderFetchResultDto report, CancellationToken cancellationToken)
    {
        // Bağlı değilse (ShopId yok / token yok / refresh süresi geçmiş) → çekim DENEMEDEN uyar + atla
        // (token sağlayıcı aksi halde NotConnected fırlatırdı; fail-fast yerine dostane rapor).
        if (string.IsNullOrWhiteSpace(channel.ShopId) || !channel.IsConnected(Clock.Now.ToUniversalTime()))
        {
            report.Warnings.Add(_l["Order:Fetch:EtsyNotConnected", channel.Name]);
            return;
        }

        var credentials = new EtsyCredentials(channel.Id, $"{channel.Keystring}:{channel.SharedSecret}", channel.ShopId);

        // Etsy shop para birimi tek olsa da defansif: receipt-başı currency_code'u id'ye çevir (kanal-başı cache).
        var currencyCache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);

        var remoteOrders = await _etsyOrderClient.GetAllOrdersAsync(credentials, cancellationToken: cancellationToken);
        foreach (var remote in remoteOrders)
        {
            var currencyUnitId = await ResolveCurrencyUnitIdByCodeAsync(remote.CurrencyCode, currencyCache, report);
            await UpsertOrderAsync(channel.CompanyId, channel.Id, SalesChannelType.Etsy, remote,
                EtsyOrderStatusMapper.Map, currencyUnitId, report);
        }

        report.ChannelsProcessed++;
    }

    // ── Tek sipariş upsert — ORDER BAŞINA AYRI UoW → hemen commit (fresh veri anında görünür) ────────────

    private async Task UpsertOrderAsync(
        Guid companyId, Guid channelId, SalesChannelType channelType, RemoteOrder remote,
        Func<string?, OrderStatus> statusMapper, Guid? tryCurrencyUnitId, OrderFetchResultDto report,
        OrderDetailSnapshot? detail = null)
    {
        report.FetchedOrders++;

        if (string.IsNullOrWhiteSpace(remote.RemoteOrderId))
        {
            // Anahtarsız uzak kayıt idempotent upsert edilemez → sessizce ATLA değil, raporla.
            report.Warnings.Add(_l["Order:Fetch:MissingRemoteId", remote.OrderNumber]);
            return;
        }

        using var uow = _uowManager.Begin(requiresNew: true);

        var fetchedAt = Clock.Now.ToUniversalTime();
        var neutralStatus = statusMapper(remote.RemoteStatus);

        // Idempotency: (SalesChannelId, RemoteOrderId) — bu order daha önce çekilmiş mi?
        var order = await AsyncExecuter.FirstOrDefaultAsync(
            (await _orderRepository.GetQueryableAsync())
                .Where(o => o.CompanyId == companyId && o.SalesChannelId == channelId && o.RemoteOrderId == remote.RemoteOrderId));

        if (order is null)
        {
            order = new Order(companyId, channelId, channelType, remote.RemoteOrderId, remote.OrderNumber);
            order.ApplyRemote(
                remote.OrderNumber, remote.OrderDate, neutralStatus, remote.RemoteStatus, remote.CustomerName,
                remote.TotalAmount, tryCurrencyUnitId, remote.CargoProvider, remote.CargoTrackingNumber, fetchedAt);
            order.SetDetail(detail);   // zengin detay (varsa) — null geçilirse dokunmaz
            await _orderRepository.InsertAsync(order, autoSave: true);
            report.NewOrders++;
        }
        else
        {
            order.ApplyRemote(
                remote.OrderNumber, remote.OrderDate, neutralStatus, remote.RemoteStatus, remote.CustomerName,
                remote.TotalAmount, tryCurrencyUnitId, remote.CargoProvider, remote.CargoTrackingNumber, fetchedAt);
            order.SetDetail(detail);
            await _orderRepository.UpdateAsync(order, autoSave: true);
            report.UpdatedOrders++;
        }

        await ReplaceLinesAsync(companyId, order.Id, remote.Lines, report);

        // Ürün versiyonu bağı (O1, task #57) — insert-only-if-missing, resync'te ZATEN eşleşmiş satırlara dokunmaz.
        await _productMatcher.MatchLinesAsync(companyId, channelId, channelType, order.Id, remote.Lines);

        await uow.CompleteAsync();
    }

    /// <summary>Satırları SİL+YAZ ile tazeler (snapshot; idempotent). Ürün adı boşsa barkod/stok koduna, o da yoksa
    /// "-" fallback'ine düşer (kalem kaybetmek daha kötü — import onarım felsefesi).</summary>
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

    /// <summary>N11 getOrderDetail'i order-başına çeker (ENRICHMENT). Başarısızlıkta (id boş / SOAP hatası / throttle)
    /// null → sipariş yine kaydolur, yalnız detay boş kalır (snapshot felsefesi: başlık/grid detaysız da tam anlamlı).</summary>
    private async Task<OrderDetailSnapshot?> FetchN11DetailSafeAsync(SalesChannelTrN11 channel, string remoteOrderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remoteOrderId))
        {
            return null;
        }

        try
        {
            var fetchedAt = Clock.Now.ToUniversalTime();
            return await _n11OrderClient.GetOrderDetailAsync(channel.AppKey, channel.AppSecret, remoteOrderId, fetchedAt, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "N11 getOrderDetail atlandı (sipariş {OrderId}) — kimlik/ağ/throttle?", remoteOrderId);
            return null;
        }
    }

    private async Task<bool> ChannelHasOrdersAsync(Guid companyId, Guid channelId)
    {
        return await AsyncExecuter.AnyAsync(
            (await _orderRepository.GetQueryableAsync()).Where(o => o.CompanyId == companyId && o.SalesChannelId == channelId));
    }

    /// <summary>TRY para birimi (HOST kaydından TENANT bağlamında; filtre-kapalı). Bulunamazsa null + rapora uyarı.</summary>
    private async Task<Guid?> ResolveTryCurrencyUnitIdAsync(OrderFetchResultDto report)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var candidates = await AsyncExecuter.ToListAsync(
                (await _currencyUnitRepository.GetQueryableAsync()).Where(c => c.Code == CurrencyUnitCode.TRY));
            var preferred = candidates.FirstOrDefault(c => c.TenantId == CurrentTenant.Id)
                            ?? candidates.FirstOrDefault(c => c.TenantId == null);
            if (preferred is null)
            {
                report.Warnings.Add(_l["Order:Fetch:TryCurrencyMissing"]);
            }

            return preferred?.Id;
        }
    }

    /// <summary>Verilen ISO para birimi KODUNU (ör. Etsy "USD"/"EUR") yerel CurrencyUnitId'ye çevirir (HOST/TENANT
    /// kaydı; filtre-kapalı). Kanal-başı <paramref name="cache"/> ile tek sorgu. Bulunamazsa null + rapora uyarı
    /// (tutar yine saklanır; yalnız para birimi bağı boş kalır). Kod boşsa null (Tr-pazaryerleri TRY yolunu kullanır).</summary>
    private async Task<Guid?> ResolveCurrencyUnitIdByCodeAsync(string? code, Dictionary<string, Guid?> cache, OrderFetchResultDto report)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (cache.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var candidates = await AsyncExecuter.ToListAsync(
                (await _currencyUnitRepository.GetQueryableAsync()).Where(c => c.Code == normalized));
            var preferred = candidates.FirstOrDefault(c => c.TenantId == CurrentTenant.Id)
                            ?? candidates.FirstOrDefault(c => c.TenantId == null);
            if (preferred is null)
            {
                report.Warnings.Add(_l["Order:Fetch:CurrencyMissing", normalized]);
            }

            cache[normalized] = preferred?.Id;
            return preferred?.Id;
        }
    }

    private async Task RunSafeAsync(Func<Task> action, Guid channelId)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Sipariş senkron atlandı (kanal {ChannelId}) — kimlik/ağ/throttle?", channelId);
        }
    }

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
}
