using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Identity;
using System.Collections.Generic;

namespace Integration.TradeXpress.Tenants;

public class TenantCreatedEventHandler
    : IDistributedEventHandler<TenantCreatedEto>, ITransientDependency
{
    private readonly IDataSeeder _dataSeeder;
    private readonly ICurrentTenant _currentTenant;

    public TenantCreatedEventHandler(IDataSeeder dataSeeder, ICurrentTenant currentTenant)
    {
        _dataSeeder = dataSeeder;
        _currentTenant = currentTenant;
    }

    public async Task HandleEventAsync(TenantCreatedEto eventData)
    {
        using (_currentTenant.Change(eventData.Id))
        {
            await _dataSeeder.SeedAsync(new DataSeedContext(eventData.Id)
                .WithProperty(IdentityDataSeedContributor.AdminEmailPropertyName,
                    eventData.Properties.GetValueOrDefault(IdentityDataSeedContributor.AdminEmailPropertyName))
                .WithProperty(IdentityDataSeedContributor.AdminPasswordPropertyName,
                    eventData.Properties.GetValueOrDefault(IdentityDataSeedContributor.AdminPasswordPropertyName)));
        }
    }
}
