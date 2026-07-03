using Serilog;
using Serilog.Events;

namespace Integration.TradeXpress.Blazor;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            // Log rotasyonu: günlük + 50MB dosya limiti + 31 dosya tut → 5-10 yıl boyunca disk dolmaz.
            .WriteTo.Async(c => c.File(
                "Logs/logs.txt",
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 50_000_000,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 31))
            .WriteTo.Async(c => c.Console())
            .CreateLogger();

        try
        {
            Log.Information("Starting web host.");
            var builder = WebApplication.CreateBuilder(args);
            builder.Host.AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog();
            await builder.AddApplicationAsync<TradeXpressBlazorModule>();
            var app = builder.Build();
            await app.InitializeApplicationAsync();
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            if (ex is HostAbortedException)
            {
                throw;
            }

            Log.Fatal(ex, "Host terminated unexpectedly!");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}