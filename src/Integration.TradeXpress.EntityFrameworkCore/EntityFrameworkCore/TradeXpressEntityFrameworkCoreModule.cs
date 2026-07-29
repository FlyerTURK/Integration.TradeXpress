using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Uow;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.SqlServer;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.TenantManagement.EntityFrameworkCore;
using Volo.Abp.Studio;

namespace Integration.TradeXpress.EntityFrameworkCore;

[DependsOn(
    typeof(TradeXpressDomainModule),
    typeof(AbpPermissionManagementEntityFrameworkCoreModule),
    typeof(AbpSettingManagementEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreSqlServerModule),
    typeof(AbpBackgroundJobsEntityFrameworkCoreModule),
    typeof(AbpAuditLoggingEntityFrameworkCoreModule),
    typeof(AbpFeatureManagementEntityFrameworkCoreModule),
    typeof(AbpIdentityEntityFrameworkCoreModule),
    typeof(AbpOpenIddictEntityFrameworkCoreModule),
    typeof(AbpTenantManagementEntityFrameworkCoreModule),
    typeof(BlobStoringDatabaseEntityFrameworkCoreModule)
    )]
public class TradeXpressEntityFrameworkCoreModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {

        TradeXpressEfCoreEntityExtensionMappings.Configure();
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAbpDbContext<TradeXpressDbContext>(options =>
        {
                /* Remove "includeAllEntities: true" to create
                 * default repositories only for aggregate roots */
            options.AddDefaultRepositories(includeAllEntities: true);

            // Kategori grafı ATOMİK yüklenir (nitelik + değerleri). Tek seviyeli WithDetailsAsync yetmez
            // (iç içe ThenInclude gerekir) ve eksik yükleme SESSİZ VERİ BOZULMASI üretirdi: güncellemede
            // merge var olan satırı bulamayıp her kaydetmede kopya nitelik eklerdi.
            // Şablon SATIRLARIYLA birlikte anlamlıdır: satırsız yüklenirse "uygula" sessizce hiçbir şey eklemez
            // ve güncellemede merge var olan satırı bulamayıp kopya üretirdi (kategori grafıyla aynı gerekçe).
            options.Entity<RecipeTemplates.RecipeTemplate>(template =>
            {
                template.DefaultWithDetailsFunc = query => query.Include(x => x.Lines);
            });

            options.Entity<ProductCategories.ProductCategory>(category =>
            {
                category.DefaultWithDetailsFunc = query =>
                    query.Include(x => x.Attributes).ThenInclude(a => a.Values);
            });
        });

        if (AbpStudioAnalyzeHelper.IsInAnalyzeMode)
        {
            return;
        }

        Configure<AbpDbContextOptions>(options =>
        {
            /* The main point to change your DBMS.
             * See also TradeXpressDbContextFactory for EF Core tooling. */

            options.UseSqlServer();

        });
        
        // NOT: AddAlwaysDisableUnitOfWorkTransaction KALDIRILDI (2026-07): o kayıt IUnitOfWorkManager'ı
        // decorator'la sarıp TÜM Begin çağrılarında IsTransactional'ı false'a eziyordu — açık
        // [UnitOfWork(isTransactional: true)] opt-in'i bile sessizce etkisizleşiyordu. Default hâlâ
        // Disabled: konvansiyonel/otomatik UoW'lar transaction'sız kalır; transaction YALNIZ açık
        // opt-in ile gelir (ör. VoucherAppService çok-adımlı yazım yolları).
        Configure<AbpUnitOfWorkDefaultOptions>(options =>
        {
            options.TransactionBehavior = UnitOfWorkTransactionBehavior.Disabled;
        });
    }
}
