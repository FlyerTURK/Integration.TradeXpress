using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Orchestration;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Trendyol;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Sipariş SENKRONİZASYON çekirdeği — pazaryerinden STREAMING (sayfa-sayfa) çekip <b>order başına kaydeder</b> (fetch
/// bitmeden fresh veri akar; kısmi başarı korunur). Auth YOK → hem <see cref="OrderAppService"/> (manuel düğme, current
/// company) hem <see cref="OrderSyncBackgroundWorker"/> buradan tüketir.
/// Worker bağlamında tenant kanaldan gelir (<see cref="CurrentTenant"/>.Change(channel.TenantId)).
///
/// <para><b>İKİ STRATEJİ:</b> <see cref="SyncEmptyChannelsAsync"/> (boş kanal → tarih filtresiz TAM geçmiş) ve
/// <see cref="SyncActiveChannelsAsync"/> (dolu kanal → dar pencere + açık siparişlerin tazelenmesi). Ortak
/// tenant/kimlik iskeleti <c>ForEachChannelAsync</c>'te; iki kol onu kopyalamaz.</para>
///
/// <para><b>⚠ Çekim SALT-OKUMA DEĞİLDİR</b> (eski doc öyle diyordu): upsert idempotenttir ama zincirin devamı
/// ürün eşleştirmesini, REZERVASYONU (yani fiş + stok) ve iptal köprüsünü tetikler. Bu yüzden delta kolunun
/// canlıda açılması bir config kararıdır, kod merge'i değil.</para>
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
    private readonly OrderReservationManager _reservationManager;
    private readonly IDataFilter _dataFilter;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly OrchestrationIdentityScope _identityScope;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly IStringLocalizer<TradeXpressResource> _l;
    private readonly OrderSyncOptions _syncOptions;

    /// <summary>Trendyol geriye-bakış: yalnız son ~1 ayı servis eder (retention) → daha geriye gitmek boşuna.</summary>
    private static readonly TimeSpan TrendyolFetchLookback = TimeSpan.FromDays(40);

    /// <summary>Delta penceresinin ÖRTÜŞME payı — pencere başı = son çekim anı − bu süre.
    /// <para>Upsert idempotent olduğu için örtüşme zararsızdır; payı KALDIRMAK ise tehlikelidir: pazaryerinin
    /// saati bizden birkaç saat farklıysa ya da bir tur hata alırsa aradaki siparişler HİÇ görünmezdi.</para></summary>
    private static readonly TimeSpan DeltaOverlap = TimeSpan.FromDays(2);

    /// <summary>Delta turunda detayı tazelenecek AÇIK sipariş tavanı — throttle bütçesi korunur.
    /// En eski <c>FetchedAt</c> önce gelir, yani sıra kimseyi aç bırakmaz (round-robin).</summary>
    private const int MaxOpenOrderRefreshPerRound = 50;

    /// <summary>Boş dönen kanalların soğuma saatleri. STATİK: manager transient/scoped çözülür, örnek-başı bir
    /// sözlük her turda sıfırlanır ve soğuma HİÇ çalışmazdı — sessizce etkisiz bir emniyet olurdu.</summary>
    private static readonly ConcurrentDictionary<Guid, DateTime> _emptyChannelCooldown = new();

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
        OrderReservationManager reservationManager,
        IDataFilter dataFilter,
        IUnitOfWorkManager uowManager,
        OrchestrationIdentityScope identityScope,
        ICurrentPrincipalAccessor currentPrincipalAccessor,
        IStringLocalizer<TradeXpressResource> l,
        IOptions<OrderSyncOptions> syncOptions)
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
        _reservationManager = reservationManager;
        _dataFilter = dataFilter;
        _uowManager = uowManager;
        _identityScope = identityScope;
        _currentPrincipalAccessor = currentPrincipalAccessor;
        _l = l;
        _syncOptions = syncOptions.Value;
    }

    // ── Worker girişi: TÜM tenant'ların BOŞ kanallarını seed'le (streaming) ────────────────────────────

    /// <summary>Tüm tenant'lardaki her N11/Trendyol kanalı için: kanalın HİÇ siparişi yoksa TÜM geçmişi streaming
    /// çeker (period doldurmadan). Tenant kanaldan gelir → her kanal kendi tenant scope'unda işlenir. Kanal başına
    /// bağımsız try/catch (biri düşse — kimlik/ağ/throttle — diğerleri çalışır, worker çökmez).</summary>
    public virtual async Task<OrderFetchResultDto> SyncEmptyChannelsAsync(CancellationToken cancellationToken = default)
    {
        return await ForEachChannelAsync(
            "seed",
            async (channels, report) =>
            {
                foreach (var channel in channels.N11)
                {
                    await SeedChannelIfEmptyAsync(channel.Id, channel.CompanyId,
                        () => StreamN11ChannelAsync(channel, report, cancellationToken), "N11");
                }

                foreach (var channel in channels.Trendyol)
                {
                    await SeedChannelIfEmptyAsync(channel.Id, channel.CompanyId,
                        () => StreamTrendyolChannelAsync(channel, report, cancellationToken), "Trendyol");
                }

                foreach (var channel in channels.Etsy)
                {
                    await SeedChannelIfEmptyAsync(channel.Id, channel.CompanyId,
                        () => StreamEtsyChannelAsync(channel, report, cancellationToken), "Etsy");
                }
            },
            cancellationToken);
    }

    /// <summary>DOLU kanalların DAR PENCERE (delta) çekimi — §6'nın "sipariş çekildiği ANDA rezerve edilir"
    /// kararının kod karşılığı.
    ///
    /// <para><b>Neden seed'den ayrı bir strateji:</b> ilk kurulum tarih filtresi OLMADAN tüm geçmişi ister
    /// (period gönderilse eski siparişler gizlenir — canlı doğrulandı). Dolu kanalı 2 dakikada bir aynı şekilde
    /// taramak ise throttle bütçesini yakar ve her turda aynı siparişleri yeniden yazardı. İki kol tek metoda
    /// sıkıştırılsaydı biri diğerinin varsayımını sessizce bozardı.</para>
    ///
    /// <para><b>Damga MIGRATION'SIZ:</b> pencere başı = kanalın <c>MAX(FetchedAt)</c> − 2 gün örtüşme payı.
    /// Upsert idempotent olduğu için örtüşme zararsızdır; yeni kolon açmak yerine var olan veriden türetilir.</para>
    ///
    /// <para><b>⚠ Pencere TEK BAŞINA YETMEZ:</b> N11'in tarih filtresi sipariş TARİHİNE bakar, statü değişimine
    /// değil — pencere dışında kalan eski bir siparişin İPTALİ listeye hiç düşmez. Bu yüzden N11 kolunda açık
    /// siparişlerin detayı ayrıca tazelenir; iptal köprüsü de o tazelemeyle beslenir.</para></summary>
    public virtual async Task<OrderFetchResultDto> SyncActiveChannelsAsync(CancellationToken cancellationToken = default)
    {
        return await ForEachChannelAsync(
            "delta",
            async (channels, report) =>
            {
                foreach (var channel in channels.N11)
                {
                    var since = await ResolveDeltaWindowStartAsync(channel.CompanyId, channel.Id);
                    if (since is null)
                    {
                        continue;   // hiç siparişi yok → seed kolunun işi, delta karışmaz
                    }

                    await RunSafeAsync(
                        () => StreamN11DeltaAsync(channel, since.Value, report, cancellationToken), channel.Id);
                }

                foreach (var channel in channels.Trendyol)
                {
                    var since = await ResolveDeltaWindowStartAsync(channel.CompanyId, channel.Id);
                    if (since is null)
                    {
                        continue;
                    }

                    await RunSafeAsync(
                        () => StreamTrendyolChannelAsync(channel, report, cancellationToken, since.Value), channel.Id);
                }

                // Etsy delta KAPSAM DIŞI (ilk dilim): canlıda 0 bağlı kanal var ve Etsy istemcisi bugün
                // tamamen salt-okuma. Kapsama alınması ayrı bir iştir; burada sessizce "yapılmış gibi"
                // görünmemesi için açıkça yazılmıştır.
            },
            cancellationToken);
    }

    /// <summary>Delta penceresinin başlangıcı: kanalın en son çekim anı − örtüşme payı.
    /// <c>null</c> = kanalın hiç siparişi yok (seed kolunun işi).</summary>
    private async Task<DateTime?> ResolveDeltaWindowStartAsync(Guid companyId, Guid channelId)
    {
        var lastFetched = await AsyncExecuter.MaxAsync(
            (await _orderRepository.GetQueryableAsync())
                .Where(o => o.CompanyId == companyId && o.SalesChannelId == channelId)
                .Select(o => (DateTime?)o.FetchedAt));

        return lastFetched is { } stamp ? stamp - DeltaOverlap : null;
    }

    /// <summary>Tenant/kimlik/şirket-filtresi iskeleti — seed ve delta kollarının ORTAK çerçevesi.
    /// <para>İki kol bu iskeleti kopyalasaydı (impersonation · <c>Disable&lt;ICompanyScoped&gt;</c> · tenant
    /// başına taze UoW) biri düzeltilip diğeri unutulurdu; bu dosyada zaten o sınıf hatanın izleri var.</para></summary>
    private async Task<OrderFetchResultDto> ForEachChannelAsync(
        string armName,
        Func<ChannelSet, OrderFetchResultDto, Task> body,
        CancellationToken cancellationToken)
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
            {
                // KİMLİK: worker principal'sız koşar; satır-eşleştirme zinciri [Authorize] DAM servislerine iner
                // (OrderLineProductSnapshotBuilder → IEntityMediaAppService) → kimliksiz her tetik
                // AbpAuthorizationException olur ve kanal seed'i yarıda kesilirdi (2026-08-01 denetim bulgusu).
                // ProductStockSyncJob deseni: tenant admin'i impersonate edilir; Change ÇAĞIRANIN frame'inde
                // (AsyncLocal kuralı — OrchestrationIdentityScope doc'u). Admin yoksa tenant atlanır, sessiz geçilmez.
                var principal = await _identityScope.BuildTenantAdminPrincipalAsync();
                if (principal is null)
                {
                    Logger.LogWarning(
                        "Sipariş senkronu ({Arm}): tenant {Tenant} için admin bulunamadı — atlandı (medya okuma yetkisi kurulamaz).",
                        armName, tenantId);
                    continue;
                }

                using (_currentPrincipalAccessor.Change(principal))
                using (var uow = _uowManager.Begin(requiresNew: true))
                {
                    var channels = new ChannelSet(
                        await AsyncExecuter.ToListAsync(await _n11ChannelRepository.GetQueryableAsync()),
                        await AsyncExecuter.ToListAsync(await _trendyolChannelRepository.GetQueryableAsync()),
                        await AsyncExecuter.ToListAsync(await _etsyChannelRepository.GetQueryableAsync()));

                    if (channels.Any)
                    {
                        Logger.LogInformation(
                            "Sipariş senkronu ({Arm}): tenant {Tenant} → {N11} N11 + {Trendyol} Trendyol + {Etsy} Etsy kanal.",
                            armName, tenantId, channels.N11.Count, channels.Trendyol.Count, channels.Etsy.Count);
                    }

                    await body(channels, report);

                    await uow.CompleteAsync();
                }
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

        // BOŞ-SONUÇ SOĞUMASI: gerçekten boş bir kanal (ör. hiç siparişi olmayan Trendyol) her turda 40 günlük
        // TAM taramaya çıkıyor ve hiçbir şey bulamıyordu — 2 dakikada bir, sonsuza kadar. Throttle bütçesi
        // buraya akıyordu. Sonucu boş kalan kanal bir süre yeniden denenmez.
        if (_emptyChannelCooldown.TryGetValue(channelId, out var until) && Clock.Now.ToUniversalTime() < until)
        {
            return;
        }

        Logger.LogInformation("Sipariş seed: {Kind} kanal {ChannelId} BOŞ → streaming başlıyor.", channelKind, channelId);
        await RunSafeAsync(stream, channelId);

        // Tur sonunda HÂLÂ boşsa soğumaya alınır. Dolduysa kayıt düşer: bir daha bu yola hiç girmeyecek.
        if (await ChannelHasOrdersAsync(companyId, channelId))
        {
            _emptyChannelCooldown.TryRemove(channelId, out _);
            return;
        }

        _emptyChannelCooldown[channelId] = Clock.Now.ToUniversalTime() + _syncOptions.EmptyChannelCooldown;
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

    private async Task StreamN11ChannelAsync(
        SalesChannelTrN11 channel, OrderFetchResultDto report, CancellationToken cancellationToken,
        DateTime? sinceUtc = null)
    {
        var tryCurrencyUnitId = await ResolveTryCurrencyUnitIdAsync(report);

        var page = 0;
        int pageCount;
        do
        {
            var result = await _n11OrderClient.GetOrdersPageAsync(
                channel.AppKey, channel.AppSecret, page, sinceUtc, cancellationToken);
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

    /// <summary>N11 DELTA turu: dar pencereli liste + AÇIK siparişlerin detay tazelemesi.
    ///
    /// <para><b>İki adım da şart.</b> Liste penceresi sipariş TARİHİNE göre filtreler; bir siparişin STATÜSÜ
    /// pencere dışında değişirse (tipik örnek: iki hafta önceki siparişin bugün iptal edilmesi) o değişiklik
    /// listeye HİÇ düşmez. Yalnız pencereye güvenen bir delta, iptalleri sessizce kaçırırdı — iptal köprüsü de
    /// hiç tetiklenmezdi.</para></summary>
    private async Task StreamN11DeltaAsync(
        SalesChannelTrN11 channel, DateTime sinceUtc, OrderFetchResultDto report, CancellationToken cancellationToken)
    {
        await StreamN11ChannelAsync(channel, report, cancellationToken, sinceUtc);
        await RefreshOpenN11OrdersAsync(channel, report, cancellationToken);
    }

    /// <summary>Yerelde AÇIK görünen N11 siparişlerinin detayını tazeler ve İPTAL TALEBİNİ yakalar.
    ///
    /// <para><b>Neden gerekli:</b> N11'in liste filtresi sipariş TARİHİNE bakar. İki hafta önce verilmiş bir
    /// siparişin bugün iptal edilmesi dar pencereye HİÇ düşmez — yalnız listeye güvenen bir delta o iptali
    /// sonsuza kadar kaçırırdı ve rezervasyon tutulmaya devam ederdi.</para>
    ///
    /// <para><b>Kaynak KALEM statüsüdür</b> (kod 51), başlık değil: <c>OrderDetailSnapshot</c> başlık statüsü
    /// TAŞIMAZ ve onu eklemek şema değişikliği gerektirirdi. İptal sinyali zaten kalem seviyesinde tanımlı
    /// olduğundan doğru kaynak burasıdır — başlık iptali de zaten liste kolundan gelir.</para>
    ///
    /// <para>Terminal siparişler DIŞARIDA: canlıdaki 106 tarihsel sipariş her turda boşuna N11'e gitmiş olurdu.
    /// Tur başına tavan + en eski önce sıralaması throttle bütçesini korur.</para></summary>
    private async Task RefreshOpenN11OrdersAsync(
        SalesChannelTrN11 channel, OrderFetchResultDto report, CancellationToken cancellationToken)
    {
        var openOrders = await AsyncExecuter.ToListAsync(
            (await _orderRepository.GetQueryableAsync())
                .Where(o => o.CompanyId == channel.CompanyId
                            && o.SalesChannelId == channel.Id
                            && (o.NeutralStatus == OrderStatus.New
                                || o.NeutralStatus == OrderStatus.Processing
                                || o.NeutralStatus == OrderStatus.Shipped
                                || o.NeutralStatus == OrderStatus.Unknown))
                .OrderBy(o => o.FetchedAt)
                .Take(MaxOpenOrderRefreshPerRound));

        foreach (var order in openOrders)
        {
            var detail = await FetchN11DetailSafeAsync(channel, order.RemoteOrderId, cancellationToken);
            if (detail is null)
            {
                continue;   // detay alınamadı → mevcut kayıt korunur (enrichment felsefesi)
            }

            order.SetDetail(detail);
            await _orderRepository.UpdateAsync(order, autoSave: true);
            report.RefreshedOrders++;

            if (detail.Items.Any(i => N11OrderStatusCatalog.IsCancellationRequested(i.Status)))
            {
                await _reservationManager.NotifyCancellationRequestedAsync(order.Id);
            }
        }
    }

    private async Task StreamTrendyolChannelAsync(
        SalesChannelTrTrendyol channel, OrderFetchResultDto report, CancellationToken cancellationToken,
        DateTime? deltaSinceUtc = null)
    {
        var tryCurrencyUnitId = await ResolveTryCurrencyUnitIdAsync(report);

        // Trendyol retention ~1 ay → seed'de tek 40 günlük pencere yeterli; client 14 günlük dilim + sayfa
        // döngüsünü içeride yapar. DELTA turunda pencere dar tutulur (son çekim − örtüşme payı).
        var sinceUtc = deltaSinceUtc ?? Clock.Now.ToUniversalTime() - TrendyolFetchLookback;
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

        // REZERVASYON (Faz 7): sipariş çekildiği ANDA maden/mamül müşteriye ayrılır. KOŞULSUZ — stok yetmese
        // bile yazılır ve Available EKSİYE düşer (defter dürüst kalır; kırpma kanal sınırında). İDEMPOTENT:
        // worker 2 dakikada bir aynı siparişle döner, zaten rezerve olan siparişte hiçbir şey yapılmaz.
        // Eşleşmeyen/reçetesiz sipariş SESSİZ ATLANMAZ: rezervasyon Blocked gerekçesiyle kaydedilir.
        await _reservationManager.EnsureReservationAsync(companyId, order.Id);

        // İPTAL KÖPRÜSÜ: kanaldan gelen iptal sinyali rezervasyonun KARAR eksenini uyandırır.
        // Köprü olmadan `RequestCancellation`'ın hiç çağıranı yoktu → karar ekseni sonsuza kadar "yok"ta
        // kalır, kullanıcı arayüzündeki Onayla/Reddet düğmeleri ölü veriye bağlanırdı.
        // Kanal-agnostik: nötr Cancelled'a üç mapper de düşer. Kalem kodu 51 ("İptal Talep Edildi") N11'e özgü
        // ek sinyaldir — sipariş başlığı hâlâ açıkken tek kalem iptal isteyebilir.
        // STOK EKSENİNE DOKUNULMAZ (§6): maden tutulmaya devam eder; kararı kullanıcı verir.
        if (neutralStatus == OrderStatus.Cancelled
            || remote.Lines.Any(l => N11OrderStatusCatalog.IsCancellationRequested(l.RemoteLineStatus)))
        {
            await _reservationManager.NotifyCancellationRequestedAsync(order.Id);
        }

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

/// <summary>Bir tenant'ın kanalları — seed ve delta kollarının ORTAK girdisi (üç listeyi ayrı ayrı taşımak
/// imza gürültüsü üretiyordu).</summary>
internal sealed record ChannelSet(
    List<SalesChannelTrN11> N11,
    List<SalesChannelTrTrendyol> Trendyol,
    List<SalesChannelEtsy> Etsy)
{
    public bool Any
    {
        get { return N11.Count > 0 || Trendyol.Count > 0 || Etsy.Count > 0; }
    }
}
