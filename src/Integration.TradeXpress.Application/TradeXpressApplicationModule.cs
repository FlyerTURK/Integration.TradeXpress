using System;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Account;
using Volo.Abp.Identity;
using Volo.Abp.Mapperly;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.TenantManagement;
using Integration.Framework;
using Integration.TradeXpress.Currencies;

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

        // HaremBridge localhost endpoint'i için named HttpClient. BaseAddress yok
        // (HaremClient absolute URL'i options.HaremBridgeUrl'den okur). Timeout kısa.
        context.Services.AddHttpClient(HaremClient.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ExchangeRateOptions>>().Value;
            client.Timeout = options.HaremHttpTimeout;
        });
    }
}
