using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Integration.TradeXpress.Data;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.EntityFrameworkCore;

public class EntityFrameworkCoreTradeXpressDbSchemaMigrator
    : ITradeXpressDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public EntityFrameworkCoreTradeXpressDbSchemaMigrator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolving the TradeXpressDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<TradeXpressDbContext>()
            .Database
            .MigrateAsync();
    }
}
