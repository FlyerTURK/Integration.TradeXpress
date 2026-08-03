using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.ChannelQuestions;
using Integration.TradeXpress.Channels;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Channels.N11;

/// <summary>
/// N11 kanalı kurulum sağlayıcısı — bir N11 kanalı kaydedilince kullanıma hazır gelmesi için gereken HOST-GLOBAL
/// referans senkronizasyonlarını sırayla, resilient (her adım bağımsız) yürütür. Adımlar:
/// <list type="number">
///   <item><b>categories</b> — kategori ağacı (<see cref="IN11CategoryAppService.SyncCategoriesAsync"/>).</item>
///   <item><b>shipment-companies</b> — kargo firmaları (<see cref="IN11ShipmentCompanyAppService.SyncAsync"/>).</item>
///   <item><b>cities</b> — il/ilçe listesi (<see cref="IN11CityAppService.SyncCitiesAndDistrictsAsync"/>).</item>
///   <item><b>questions-seed</b> — ilk soru çekimini SIRAYA alır (N11'e gitmez; bkz. adımın kendi gerekçesi).</item>
/// </list>
/// <para><b>Kimlik ön-koşulu (Etsy shop-scoped deseninin N11 karşılığı):</b> N11 referans verisi PER-KANAL değil,
/// HOST hesabıyla çekilir (config <c>N11:CategorySync:AppKey/AppSecret</c> — kategori/kargo/şehir ortak). Bu yüzden
/// per-kanal token GEREKMEZ; ön-koşul host kimliğinin YAPILANDIRILMIŞ olmasıdır. Yoksa üç adım da dostane Skipped
/// (throw YOK). N11 ürün IMPORTU yok → import adımı da yok.</para>
/// <para><b>Host bağlamı:</b> referans verisi host-global (tüm tenant'lar paylaşır, tek DB). City/Shipment sync'leri
/// host-only guard'lıdır (tenant bağlamında throw eder); kanal-create bir TENANT işlemi olduğundan her sync adımı
/// <c>CurrentTenant.Change(null)</c> ile host bağlamına sabitlenir — bu, ilgili AppService'lerin kendi içlerinde ve
/// <c>N11ReferenceSyncWorker</c>'ın host bağlamında kullandığı aynı desendir (data-filter DISABLE DEĞİL, host'a geçiş).</para>
/// </summary>
public class N11ChannelProvisioner : ChannelProvisionerBase
{
    private readonly IConfiguration _configuration;
    private readonly ICurrentTenant _currentTenant;
    private readonly IN11CategoryAppService _categoryAppService;
    private readonly IN11ShipmentCompanyAppService _shipmentCompanyAppService;
    private readonly IN11CityAppService _cityAppService;
    private readonly ChannelQuestionSyncManager _questionSyncManager;
    private readonly OrderSyncManager _orderSyncManager;
    private readonly IRepository<SalesChannelBase, Guid> _channelRepository;

    public N11ChannelProvisioner(
        IStringLocalizer<TradeXpressResource> localizer,
        ILogger<N11ChannelProvisioner> logger,
        IConfiguration configuration,
        ICurrentTenant currentTenant,
        IN11CategoryAppService categoryAppService,
        IN11ShipmentCompanyAppService shipmentCompanyAppService,
        IN11CityAppService cityAppService,
        ChannelQuestionSyncManager questionSyncManager,
        OrderSyncManager orderSyncManager,
        IRepository<SalesChannelBase, Guid> channelRepository)
        : base(localizer, logger)
    {
        _configuration = configuration;
        _currentTenant = currentTenant;
        _categoryAppService = categoryAppService;
        _shipmentCompanyAppService = shipmentCompanyAppService;
        _cityAppService = cityAppService;
        _questionSyncManager = questionSyncManager;
        _orderSyncManager = orderSyncManager;
        _channelRepository = channelRepository;
    }

    public override SalesChannelType ChannelType
    {
        get
        {
            return SalesChannelType.TrN11;
        }
    }

    public override async Task<ProvisioningResultDto> ProvisionAsync(Guid channelId, CancellationToken cancellationToken)
    {
        // Ön-koşul bir kez çözülür: host kimliği yapılandırılmış mı? (per-kanal token yok — N11 referansı host-global).
        var hostCredentialsPresent = HasHostCredentials();

        var steps = new List<ProvisioningStepResultDto>
        {
            await RunHostSyncStepAsync(
                "categories",
                L["ChannelProvisioning:N11:Step:Categories"],
                hostCredentialsPresent,
                () => _categoryAppService.SyncCategoriesAsync()),
            await RunHostSyncStepAsync(
                "shipment-companies",
                L["ChannelProvisioning:N11:Step:ShipmentCompanies"],
                hostCredentialsPresent,
                () => _shipmentCompanyAppService.SyncAsync()),
            await RunHostSyncStepAsync(
                "cities",
                L["ChannelProvisioning:N11:Step:Cities"],
                hostCredentialsPresent,
                () => _cityAppService.SyncCitiesAndDistrictsAsync()),
            await FetchOrderHistoryStepAsync(channelId, cancellationToken),
            await QueueFirstQuestionFetchStepAsync(channelId),
        };

        return BuildResult(channelId, steps);
    }

    /// <summary>Kanalın TÜM sipariş geçmişini kurulum sırasında çeker (2026-08-02 Hakan isteği: "kanal eklenince
    /// siparişler de gelsin" — daha önce yalnız elle 'Siparişleri Çek' butonu ya da arka plan turu dolduruyordu).
    ///
    /// <para><b>Neden senkron ve tam geçmiş:</b> canlı ölçüm (research/n11-questions/canli-kesif-2026-08-01.md)
    /// sipariş listesinde tarih-aralığı sınırı ve hız kotası OLMADIĞINI, toplamın ilk yanıtın
    /// <c>pageCount</c>'unda geldiğini gösterdi — 106 siparişlik gerçek hesapta çekim saniyeler sürdü. Soruların
    /// aksine (dakikada-1 kota → kuyruk) siparişte bekletmeye gerek yok.</para>
    ///
    /// <para><b>Idempotent:</b> upsert anahtarı (SalesChannelId, OrderNumber) — kurulum yeniden koşarsa mevcut
    /// siparişler güncellenir, kopya oluşmaz. Trendyol/Etsy provisioner'larına da aynı desen uygulanabilir
    /// (<see cref="OrderSyncManager.SyncSingleChannelAsync"/> kanal-agnostik) — o kanallar bağlandığında.</para></summary>
    private async Task<ProvisioningStepResultDto> FetchOrderHistoryStepAsync(Guid channelId, CancellationToken cancellationToken)
    {
        return await RunStepAsync(
            "orders-seed",
            L["ChannelProvisioning:N11:Step:Orders"],
            async () =>
            {
                // Kanalın sahibi şirket — SyncSingleChannelAsync kanal aidiyetini (CompanyId) filtreyle doğrular.
                var channel = await _channelRepository.GetAsync(channelId);

                var report = new OrderFetchResultDto();
                await _orderSyncManager.SyncSingleChannelAsync(channel.CompanyId, channelId, report, cancellationToken);
                return StepOutcome.Success(L["ChannelProvisioning:N11:OrdersFetched", report.FetchedOrders, report.NewOrders]);
            });
    }

    /// <summary>Kanalın ilk soru çekimini SIRAYA alır — bu adım N11'e GİTMEZ ve anında döner.
    ///
    /// <para><b>Neden çekmiyor:</b> N11 ürün sorularını hesap başına DAKİKADA BİR KEZ listelemeye izin verir ve
    /// bu kotayı eşzamanlılık aşmaz. "Tüm geçmiş" ay ay + sıralı çekildiği için ilk dolum DAKİKALAR sürer; adım
    /// bunu burada yapsaydı kurulum ekranı o süre boyunca kilitlenir ve kotayı işçiden çalardı. Bu yüzden adım
    /// yalnız işareti bırakır; çekimi <c>ChannelQuestionSyncManager</c> kuyruğu sırayla yürütür.</para>
    ///
    /// <para><b>Host bağlamına GEÇİLMEZ</b> (referans sync'lerinin aksine): soru kayıtları per-tenant ve
    /// company-owned'dır — işaret, kanalı oluşturan tenant bağlamında bırakılmalıdır.</para>
    ///
    /// <para>Host kimliği ön-koşulu da SORULMAZ: soru çekimi kanalın KENDİ kimliğiyle yapılır, host
    /// <c>N11:CategorySync:*</c> config'iyle değil. Kimlik eksikse bunu çekim turu raporlar.</para></summary>
    private async Task<ProvisioningStepResultDto> QueueFirstQuestionFetchStepAsync(Guid channelId)
    {
        return await RunStepAsync(
            "questions-seed",
            L["ChannelProvisioning:N11:Step:Questions"],
            async () =>
            {
                await _questionSyncManager.RequestPriorityAsync(channelId);
                return StepOutcome.Success(L["ChannelProvisioning:N11:QuestionSyncQueued"]);
            });
    }

    /// <summary>Host-global bir N11 referans sync'ini resilient yürütür: host kimliği yoksa dostane Skipped; varsa
    /// host bağlamına (<c>Change(null)</c>) sabitleyip çalıştırır ve değişen kayıt sayısını Success mesajına yazar.</summary>
    private async Task<ProvisioningStepResultDto> RunHostSyncStepAsync(
        string stepKey, string title, bool hostCredentialsPresent, Func<Task<int>> sync)
    {
        return await RunStepAsync(
            stepKey,
            title,
            async () =>
            {
                if (!hostCredentialsPresent)
                {
                    return StepOutcome.Skipped(L["ChannelProvisioning:N11:HostCredentialsMissing"]);
                }

                // Referans verisi host-global — city/shipment sync'leri host-only guard'lı; tenant-initiated
                // kanal-create'ten host bağlamına geç (AppService'lerin kendi iç deseniyle aynı; worker ile tutarlı).
                using (_currentTenant.Change(null))
                {
                    var changed = await sync();
                    return StepOutcome.Success(L["ChannelProvisioning:N11:Synced", changed]);
                }
            });
    }

    /// <summary>N11 host kimliği (kategori/kargo/şehir sync'lerinin ortak <c>N11:CategorySync:*</c> config'i)
    /// yapılandırılmış mı — ağ turu ATMADAN yalnız config okunur (Etsy'nin domain durumu okuma deseninin karşılığı).</summary>
    private bool HasHostCredentials()
    {
        var appKey = _configuration["N11:CategorySync:AppKey"];
        var appSecret = _configuration["N11:CategorySync:AppSecret"];
        return !string.IsNullOrWhiteSpace(appKey) && !string.IsNullOrWhiteSpace(appSecret);
    }
}
