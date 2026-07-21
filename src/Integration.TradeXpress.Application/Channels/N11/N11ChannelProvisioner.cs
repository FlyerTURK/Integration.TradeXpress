using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Channels;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.SalesChannels;
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

    public N11ChannelProvisioner(
        IStringLocalizer<TradeXpressResource> localizer,
        ILogger<N11ChannelProvisioner> logger,
        IConfiguration configuration,
        ICurrentTenant currentTenant,
        IN11CategoryAppService categoryAppService,
        IN11ShipmentCompanyAppService shipmentCompanyAppService,
        IN11CityAppService cityAppService)
        : base(localizer, logger)
    {
        _configuration = configuration;
        _currentTenant = currentTenant;
        _categoryAppService = categoryAppService;
        _shipmentCompanyAppService = shipmentCompanyAppService;
        _cityAppService = cityAppService;
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
        };

        return BuildResult(channelId, steps);
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
