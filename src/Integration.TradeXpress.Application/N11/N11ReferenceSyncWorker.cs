using System;
using System.Threading.Tasks;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.N11Shipments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace Integration.TradeXpress.N11;

/// <summary>
/// N11 host-global REFERANS verisini periyodik (nightly, 24s) RE-SYNC eden arka plan işçisi — CityService/ShipmentCompany
/// "çok aktif" (iller sabit ama ilçeler + kargo firmaları değişebiliyor). İl/ilçe + kargo firmalarını ekle/güncelle/SİL
/// ile tazeler (kategori ağacı ayrı; mahalle/attribute on-demand). Host kimliği config'ten (<c>N11:CategorySync</c>).
/// Her sync bağımsız try/catch — biri düşse (kimlik/ağ) diğeri çalışır, worker çökmez. YALNIZ Blazor host'ta kayıtlı.
/// </summary>
public class N11ReferenceSyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    public N11ReferenceSyncWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromHours(24).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        await RunSafe(workerContext, "il/ilçe", sp => sp.GetRequiredService<IN11CityAppService>().SyncCitiesAndDistrictsAsync());
        await RunSafe(workerContext, "kargo firması", sp => sp.GetRequiredService<IN11ShipmentCompanyAppService>().SyncAsync());
    }

    private async Task RunSafe(PeriodicBackgroundWorkerContext context, string label, Func<IServiceProvider, Task<int>> sync)
    {
        try
        {
            var changed = await sync(context.ServiceProvider);
            Logger.LogInformation("N11 {Label} re-sync tamam: {Changed} değişiklik.", label, changed);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "N11 {Label} re-sync atlandı (kimlik/ağ?).", label);
        }
    }
}
