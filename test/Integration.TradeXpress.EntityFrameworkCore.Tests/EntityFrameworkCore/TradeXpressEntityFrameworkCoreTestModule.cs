using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;

namespace Integration.TradeXpress.EntityFrameworkCore;

[DependsOn(
    typeof(TradeXpressApplicationTestModule),
    typeof(TradeXpressEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreSqliteModule)
)]
public class TradeXpressEntityFrameworkCoreTestModule : AbpModule
{
    private SqliteConnection? _sqliteConnection;

    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<AbpSqliteOptions>(x => x.BusyTimeout = null);
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<FeatureManagementOptions>(options =>
        {
            options.SaveStaticFeaturesToDatabase = false;
            options.IsDynamicFeatureStoreEnabled = false;
        });
        Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = false;
        });
        // FLAKY FIX (2026-07): Feature/Permission gibi SettingManagement'ın arka plan yazıcısı da KAPALI olmalı.
        // Statik setting tanımlarını DB'ye kaydeden görev ayrı thread'de KENDİ transactional UoW'unu açıyor;
        // paylaşımlı in-memory Sqlite bağlantısında seed sorgularıyla yarışıp rastgele
        // "pending local transaction" hatası üretiyordu (transaction'lar Faz 2d'de aktifleşince görünür oldu).
        Configure<SettingManagementOptions>(options =>
        {
            options.SaveStaticSettingsToDatabase = false;
            options.IsDynamicSettingStoreEnabled = false;
        });
        // NOT: AddAlwaysDisableUnitOfWorkTransaction KALDIRILDI (2026-07) — üretim modülüyle hizalı:
        // decorator açık transaction opt-in'ini de eziyordu. Sqlite in-memory (paylaşımlı bağlantı,
        // tek DbContext) transaction'ları destekler; rollback regresyonu VoucherTransactionRollbackTests'te.

        ConfigureInMemorySqlite(context.Services);

    }

    private void ConfigureInMemorySqlite(IServiceCollection services)
    {
        _sqliteConnection = CreateDatabaseAndGetConnection();

        services.Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(context =>
            {
                context.DbContextOptions.UseSqlite(_sqliteConnection);
            });
        });
    }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        _sqliteConnection?.Dispose();
    }

    private static SqliteConnection CreateDatabaseAndGetConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TradeXpressDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var context = new TradeXpressDbContext(options))
        {
            context.GetService<IRelationalDatabaseCreator>().CreateTables();
        }

        return connection;
    }
}
