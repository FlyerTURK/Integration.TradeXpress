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
        // NOT: eski HaremBridge HttpClient kaydı kaldırıldı — feed artık in-process Playwright
        // (HaremPlaywrightFeedWorker); HTTP köprü yolu ölü koddu (keşif turu 2, O5).
    }
}
