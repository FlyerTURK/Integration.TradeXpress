using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Geography;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.N11Shipments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

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

        // AÇILIŞTA DA KOŞ — yoksa ilk senkron 24 saat SONRA olurdu ve temiz kurulumda il/ilçe kataloğu
        // bir gün boyunca BOŞ kalırdı (adres formunda ilçe combosu doldurulamaz). Kurulum günü işe yaramayan
        // bir "gecelik tazeleme" anlamsızdı. Senkron upsert (idempotent) + RunSafe sarmalı → tekrar koşması
        // zararsız, düşerse worker çökmez. Arka plan işçisi olduğu için host açılışını BLOKLAMAZ.
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        await RunSafe(workerContext, "il/ilçe", sp => sp.GetRequiredService<IN11CityAppService>().SyncCitiesAndDistrictsAsync());
        // İl/ilçe re-sync'inden SONRA çekirdek coğrafyayı tazeler (N11 aynası → AdministrativeArea/Locality köprüsü).
        // Kargo tarafındaki "sync → çekirdek eşleme" deseninin birebir aynısı. Bu adım OLMADAN N11'in eklediği yeni
        // ilçe ayna tablosunda kalır, adres picker'ına DÜŞMEZ — köprü yalnız DbMigrator'da koşuyordu ve biri elle
        // çalıştırana kadar katalog bayat kalırdı. Bağımsız try/catch → düşse N11 sync'ini etkilemez.
        await RunSafe(workerContext, "coğrafya çekirdek eşleme", ReconcileCoreGeographyAsync);
        await RunSafe(workerContext, "kargo firması", sp => sp.GetRequiredService<IN11ShipmentCompanyAppService>().SyncAsync());
    }

    /// <summary>Çekirdek coğrafya eşlemesini (GeographySeeder) çalıştırır — N11 il/ilçe aynasından
    /// AdministrativeArea/Locality köprüsünü kurar. TrCarrier eşlemesiyle aynı UoW gerekçesi (aşağıya bkz.).
    /// Seeder idempotent: mevcut satırı yeniden eklemez, yalnız eksik köprüyü tamamlar.</summary>
    private static async Task<int> ReconcileCoreGeographyAsync(IServiceProvider serviceProvider)
    {
        var unitOfWorkManager = serviceProvider.GetRequiredService<IUnitOfWorkManager>();
        using var uow = unitOfWorkManager.Begin();
        await serviceProvider.GetRequiredService<GeographySeeder>().SeedAsync();
        await uow.CompleteAsync();
        return 0;   // seeder sayı döndürmez; ayrıntı kendi log'unda (il/ilçe adedi)
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
            // Etiket "kimlik/ağ?" DEMİYOR artık: 2026-07-26'da kargo sync'i aylarca bu yanıltıcı mesajla
            // yutuldu; gerçek sebep domain doğrulamasıydı. Sebep tahmini yerine exception'ın kendisi konuşsun.
            Logger.LogWarning(ex, "N11 {Label} re-sync BAŞARISIZ.", label);
        }
    }
}
