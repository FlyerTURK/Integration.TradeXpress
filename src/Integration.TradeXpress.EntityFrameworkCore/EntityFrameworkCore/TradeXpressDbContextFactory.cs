using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Integration.TradeXpress.EntityFrameworkCore;

/* This class is needed for EF Core console commands
 * (like Add-Migration and Update-Database commands) */
public class TradeXpressDbContextFactory : IDesignTimeDbContextFactory<TradeXpressDbContext>
{
    public TradeXpressDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();
        
        TradeXpressEfCoreEntityExtensionMappings.Configure();

        var builder = new DbContextOptionsBuilder<TradeXpressDbContext>()
            .UseSqlServer(configuration.GetConnectionString("Default"));
        
        return new TradeXpressDbContext(builder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../Integration.TradeXpress.DbMigrator/"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables();

        return builder.Build();
    }
}
