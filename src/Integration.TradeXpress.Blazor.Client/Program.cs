using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Integration.TradeXpress.Blazor.Client.Globalization;

namespace Integration.TradeXpress.Blazor.Client;

public class Program
{
    public async static Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        var application = await builder.AddApplicationAsync<TradeXpressBlazorClientModule>(options =>
        {
            options.UseAutofac();
        });

        var host = builder.Build();

        // Kullanıcının seçtiği UI kültürünü (localStorage) tarayıcı varsayılanından ÖNCE uygula.
        // WASM'da .AspNetCore.Culture cookie'si yalnız sunucuyu etkiler; istemci UI kültürü
        // CultureInfo üzerinden burada set edilmeli (ABP IStringLocalizer bunu okur).
        await ApplyStoredCultureAsync(host);

        await application.InitializeApplicationAsync(host.Services);

        await host.RunAsync();
    }

    private static async Task ApplyStoredCultureAsync(WebAssemblyHost host)
    {
        try
        {
            var js = host.Services.GetRequiredService<IJSRuntime>();
            var name = await js.InvokeAsync<string?>("localStorage.getItem", CultureCatalog.StorageKey);
            if (!string.IsNullOrWhiteSpace(name))
            {
                var culture = new CultureInfo(name);
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
        }
        catch (Exception)
        {
            // Okunamazsa tarayıcı kültürüne düş — sessiz geç.
        }
    }
}
