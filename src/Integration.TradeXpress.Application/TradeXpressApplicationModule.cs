using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Account;
using Volo.Abp.Identity;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.TenantManagement;
using Integration.Framework;
using Integration.TradeXpress.Financials.ExchangeRates;
using Integration.TradeXpress.N11Products;

namespace Integration.TradeXpress;

[DependsOn(
    typeof(TradeXpressDomainModule),
    typeof(TradeXpressApplicationContractsModule),
    typeof(IntegrationFrameworkApplicationModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpFeatureManagementApplicationModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpAccountApplicationModule),
    typeof(AbpTenantManagementApplicationModule),
    typeof(AbpSettingManagementApplicationModule)
    )]
public class TradeXpressApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        context.Services.Configure<ExchangeRateOptions>(
            configuration.GetSection(ExchangeRateOptions.SectionName));

        // N11 uç adresleri — bölüm yoksa varsayılan https://api.n11.com (davranış bugünküyle birebir aynı).
        // Sahte sunucu bu tabanı kendine çevirerek çalışır; gerçek/mock geçişi TEK config değeridir.
        context.Services.Configure<N11EndpointOptions>(
            configuration.GetSection(N11EndpointOptions.SectionName));

        // Sipariş senkronu — DELTA kolu VARSAYILAN KAPALI. Bölüm hiç yoksa da kapalıdır (bool default false):
        // canlı pazaryerine 2 dakikada bir çıkan bir worker, config'in unutulmasıyla değil ancak açık bir
        // kararla başlamalıdır.
        context.Services.Configure<Orders.OrderSyncOptions>(
            configuration.GetSection(Orders.OrderSyncOptions.SectionName));
        // NOT: eski HaremBridge HttpClient kaydı kaldırıldı — feed artık in-process Playwright
        // (HaremPlaywrightFeedWorker); HaremBridge HTTP yolu ölü koddu (keşif turu 2, O5).
    }
}
