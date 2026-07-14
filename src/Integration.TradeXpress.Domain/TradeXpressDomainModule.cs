using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.Identity;
using Volo.Abp.SettingManagement;
using Volo.Abp.BlobStoring.Database;
using Volo.Abp.Caching;
using Volo.Abp.OpenIddict;
using Volo.Abp.PermissionManagement.OpenIddict;
using Volo.Abp.AuditLogging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Emailing;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.TenantManagement;
using Integration.Framework;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Products;
using Volo.Abp.BlobStoring;

namespace Integration.TradeXpress;

[DependsOn(
    typeof(TradeXpressDomainSharedModule),
    typeof(IntegrationFrameworkDomainModule),
    typeof(AbpAuditLoggingDomainModule),
    typeof(AbpCachingModule),
    typeof(AbpBackgroundJobsDomainModule),
    typeof(AbpFeatureManagementDomainModule),
    typeof(AbpPermissionManagementDomainIdentityModule),
    typeof(AbpPermissionManagementDomainOpenIddictModule),
    typeof(AbpSettingManagementDomainModule),
    typeof(AbpEmailingModule),
    typeof(AbpIdentityDomainModule),
    typeof(AbpOpenIddictDomainModule),
    typeof(AbpTenantManagementDomainModule),
    typeof(BlobStoringDatabaseDomainModule)
    )]
public class TradeXpressDomainModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpMultiTenancyOptions>(options =>
        {
            options.IsEnabled = MultiTenancyConsts.IsEnabled;
        });

        Configure<Volo.Abp.Timing.AbpClockOptions>(options =>
        {
            options.Kind = DateTimeKind.Utc;
        });

        // Ürün + maden görselleri blob konteynerleri → Database provider (AppBlobs tablosu; DbContext ConfigureBlobStoring hazır).
        Configure<AbpBlobStoringOptions>(options =>
        {
            options.Containers.Configure<ProductImagesContainer>(container =>
            {
                container.UseDatabase();
            });
            options.Containers.Configure<MetalImagesContainer>(container =>
            {
                container.UseDatabase();
            });
            // Entity-agnostik görsel container'ı (Good/GoodVariant/… + ileride Product/Metal buraya taşınır).
            options.Containers.Configure<Integration.TradeXpress.Attachments.EntityImagesContainer>(container =>
            {
                container.UseDatabase();
            });
            // Entity-agnostik doküman container'ı (ham blob; thumbnail yok).
            options.Containers.Configure<Integration.TradeXpress.Attachments.EntityDocumentsContainer>(container =>
            {
                container.UseDatabase();
            });
        });

#if DEBUG
        context.Services.Replace(ServiceDescriptor.Singleton<IEmailSender, NullEmailSender>());
#endif
    }
}
