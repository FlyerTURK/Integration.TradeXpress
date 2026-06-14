using Integration.TradeXpress.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace Integration.TradeXpress.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(TradeXpressEntityFrameworkCoreModule),
    typeof(TradeXpressApplicationContractsModule)
)]
public class TradeXpressDbMigratorModule : AbpModule
{
}
