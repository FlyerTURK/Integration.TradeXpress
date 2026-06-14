using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Data;

/* This is used if database provider does't define
 * ITradeXpressDbSchemaMigrator implementation.
 */
public class NullTradeXpressDbSchemaMigrator : ITradeXpressDbSchemaMigrator, ITransientDependency
{
    public Task MigrateAsync()
    {
        return Task.CompletedTask;
    }
}
