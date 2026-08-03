using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;

namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// Müşteri sorusu senkron işçisi — pazaryerine giden TEK MERKEZ. Her turda
/// <see cref="ChannelQuestionSyncManager.RunOnePassAsync"/> ile TEK iş adımı yürütür.
///
/// <para><b>Periyot = 1 DAKİKA çünkü kota penceresi 1 dakikadır</b> (canlı keşif: "Ürün soruları 1 dakikada bir
/// kez listelenebilmektedir"). Daha sık dönmek <c>accessLimit</c> üretir, daha seyrek dönmek hakkımızı çöpe atar.</para>
///
/// <para><b>İNCELİK — "5 dakikada bir tazeleme" kararı worker periyodu DEĞİLDİR.</b> Periyot 5 dakikaya
/// çekilseydi geçmiş seedi 5 kat yavaşlar (60 aylık geçmiş 5 saate çıkar) ve aradaki 4 dakikalık kota hakkı
/// kullanılmadan yanardı. Bunun yerine tazeleme sıklığı KANAL BAŞINA bir eşikle sağlanır
/// (<see cref="ChannelQuestionSyncConsts.RoutineRefreshMinutes"/>): worker her dakika bir adım atar, ama bir
/// kanalın rutin tazelemesi ancak son tazelemeden 5 dakika sonra aday olur. Sonuç: seed hızlı ilerler, rutin
/// tazeleme 5 dakikada bir gerçekleşir, kota hiç aşılmaz.</para>
///
/// <para><see cref="AbpAsyncTimer.RunOnStart"/>=true → ilk tur uygulama ayağa kalkar kalkmaz (timer thread'inde,
/// host açılışını bloklamaz). YALNIZ Blazor host'ta kayıtlı → çift-çalışma yok (iki süreçte kayıt, aynı dakikada
/// iki çağrı = garanti kota hatası demek olurdu).</para>
/// </summary>
public class ChannelQuestionSyncWorker : AsyncPeriodicBackgroundWorkerBase
{
    public ChannelQuestionSyncWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromMinutes(1).TotalMilliseconds;   // = kota penceresi (bkz. sınıf özeti)
        Timer.RunOnStart = true;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        try
        {
            var manager = workerContext.ServiceProvider.GetRequiredService<ChannelQuestionSyncManager>();

            // UoW yönetimi TAMAMEN manager'da: tenant Change'i sonrası taze requiresNew UoW + uzak çağrı UoW
            // dışında (yavaş SOAP isteği DbContext'i tutmasın).
            var written = await manager.RunOnePassAsync(workerContext.CancellationToken);
            if (written > 0)
            {
                Logger.LogInformation("Soru senkronu: {Count} soru yazıldı (bu tur).", written);
            }
        }
        catch (Exception ex)
        {
            // Döngü ÖLMESİN: kimlik/ağ/ayrıştırma hatası bir turu düşürür, sistemi değil. İlerleme yazılmadığı
            // için aynı adım bir sonraki turda yeniden denenir.
            Logger.LogWarning(ex, "Soru senkron turu atlandı (kimlik/ağ?).");
        }
    }
}
