using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Integration.TradeXpress.Data;
using Serilog;
using Volo.Abp;
using Volo.Abp.Data;

namespace Integration.TradeXpress.DbMigrator;

public class DbMigratorHostedService : IHostedService
{
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly IConfiguration _configuration;

    public DbMigratorHostedService(IHostApplicationLifetime hostApplicationLifetime, IConfiguration configuration)
    {
        _hostApplicationLifetime = hostApplicationLifetime;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using (var application = await AbpApplicationFactory.CreateAsync<TradeXpressDbMigratorModule>(options =>
        {
           options.Services.ReplaceConfiguration(_configuration);
           options.UseAutofac();
           options.Services.AddLogging(c => c.AddSerilog());
           options.AddDataMigrationEnvironment();
        }))
        {
            await application.InitializeAsync();

            await application
                .ServiceProvider
                .GetRequiredService<TradeXpressDbMigrationService>()
                .MigrateAsync();

            using (var scope = application.ServiceProvider.CreateScope())
            {
                var metalRepo = scope.ServiceProvider.GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<Integration.TradeXpress.Metals.Metal, System.Guid>>();
                var variantRepo = scope.ServiceProvider.GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<Integration.TradeXpress.Variants.EntityVariant, System.Guid>>();
                var currentTenant = scope.ServiceProvider.GetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>();
                var currentCompany = scope.ServiceProvider.GetRequiredService<Integration.TradeXpress.MultiCompany.ICurrentCompany>();

                var tenantId = new System.Guid("04EE976E-207F-F97D-846C-3A223EBF4167");
                var companyId = new System.Guid("9BC09D32-377B-AA13-596D-3A223EBF4A5B");

                using (currentTenant.Change(tenantId))
                using (currentCompany.Change(companyId))
                {
                    var metalPredicate = Integration.TradeXpress.MultiCompany.CompanyScopedQueryable.CompanyVisiblePredicate<Integration.TradeXpress.Metals.Metal>(currentTenant.Id, currentCompany.Id);
                    var variantPredicate = Integration.TradeXpress.MultiCompany.CompanyScopedQueryable.CompanyVisiblePredicate<Integration.TradeXpress.Variants.EntityVariant>(currentTenant.Id, currentCompany.Id);

                    var uowManager = scope.ServiceProvider.GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>();
                    using (var uow = uowManager.Begin(new Volo.Abp.Uow.AbpUnitOfWorkOptions(), true))
                    {
                        var metalsQuery = await metalRepo.GetQueryableAsync();
                        var variantsQuery = await variantRepo.GetQueryableAsync();

                        var baseQuery = System.Linq.Queryable.Where(
                            System.Linq.Queryable.Join(
                                System.Linq.Queryable.Where(metalsQuery, metalPredicate),
                                System.Linq.Queryable.Where(variantsQuery, variantPredicate),
                                metal => metal.Id,
                                variant => variant.EntityId,
                                (metal, variant) => new { metal, variant }
                            ),
                            x => x.variant.EntityName == "Metal" && !x.variant.IsDeleted && !x.metal.IsDeleted
                        );

                        var result = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(baseQuery);
                        Log.Information("TEST COUNT: {Count}", result.Count);
                        foreach (var item in result) {
                            Log.Information("Found: {Code}", item.metal.Code);
                        }
                    }
                }
            }

            await application.ShutdownAsync();

            _hostApplicationLifetime.StopApplication();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
