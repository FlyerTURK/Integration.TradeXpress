using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.N11Categories;

/// <summary>N11 sentetik mega üst-katmanını uygular (DbMigrator/seed yolu). Kategori ağacı HOST-GLOBAL → yalnız host
/// pass'te çalışır (grouper zaten <c>CurrentTenant.Change(null)</c> yapar). İdempotent: 79 top zaten synced'se
/// meganın altına bağlar; henüz synced değilse 9 mega'yı kurar, ilk sync sonunda <c>SyncCategoriesAsync</c> bağlar.</summary>
public class N11MegaCategorySeeder : IDataSeedContributor, ITransientDependency
{
    private readonly N11CategoryMegaGrouper _grouper;

    public N11MegaCategorySeeder(N11CategoryMegaGrouper grouper)
    {
        _grouper = grouper;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId is not null)
        {
            return;   // host-global veri → tenant pass'inde çalıştırma
        }

        await _grouper.EnsureAsync();
    }
}
