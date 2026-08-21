using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannelProducts;
using Integration.TradeXpress.SalesChannels;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// PASİFLEŞEN N11 KANAL ÜRÜNÜNÜN SATIŞINI DURDURUR — bilinen TÜM SKU'lara ADET-0 gönderir
/// (2026-08-21 Hakan kararı: <i>"Tabi ki isactive false ise derhal 0 stok olmalı"</i>).
/// <c>TrendyolListingWithdrawer.SetArchivedAsync</c>'in N11 karşılığıdır; fark kanalın yeteneğinden gelir:
/// N11'in uzak arşiv ucu YOK, satışı durdurmanın tek yolu fiyat/stok ucundan adet-0 yazmaktır.
///
/// <para><b>Neden gerekliydi:</b> <c>PassiveNoSync</c> guard'ı + stok tetiği süzgeci pasif kayda YENİ yazımı
/// keser ama SON GÖNDERİLEN adet N11'de canlı kalır — o listelemeden sipariş gelebilir. Pasifleşme ANINDA
/// adet-0 gitmezse "kaldırdım" sanılan ürün satılmaya devam eder (oversell → pazaryeri cezası).</para>
///
/// <para><b>Fiyat KORUNUR</b> (son gönderilen değer): amaç satışı durdurmak, listelemeyi kapatmak DEĞİL —
/// N11'de "Out_Of_Stock" görünür. Geri dönüş için ayrı bir "yeniden aç" yolu YOKTUR: aktifleşen kayıt senkron
/// kapsamına geri girer ve dirty-check (<c>LastSentQuantity=0</c> ≠ gerçek) gerçek adedi kendiliğinden yazar.</para>
///
/// <para><b>Geri dönüş semantiği (Trendyol emsali):</b> çağıran bayrağı yazdıktan SONRA, aynı UoW'da çağırır —
/// N11 (ya da ağ) reddederse istisna yukarı çıkar ve pasifleştirme GERİ DÖNER: biz ile kanal farklı şey
/// söyleyemez ("bizde pasif ama N11'de stoklu satışta" tam da kapatılmak istenen delik).</para>
///
/// <para><b>Kuyruk kapısı:</b> kayıtta çözülmemiş bir push task'ı varsa adet-0 GÖNDERİLMEZ
/// (<c>TradeXpress:N11:Rest:PushPending</c>) — iki açık task'tan hangisinin son yazdığı belirsizdir; bekleyen
/// task gerçek adetli bir tam-push ise adet-0'ın ÜSTÜNE yazabilirdi. Kuyruk işçisi (5 dk) task'ı çözünce
/// pasifleştirme yeniden denenebilir.</para>
///
/// <para><b>Kendi gönderimi kuyruğa düşerse:</b> <c>MarkPushQueued</c> ile task kimliği saklanır;
/// <c>N11PendingPushResolver</c> pasif kayıtları da kapsar ve çözüm PASİF kayıtta LastSent'i 0'a çeker
/// (<c>ResolvePendingPushInternalAsync</c>'in pasif dalı) — plan dondurma yanlış olurdu, kanala giden 0'dı.
/// Delil satırı yalnız SONUÇ anında yazılır (2026-08-10 kuralı); başarısızlık <c>LastSent*</c>'i TERFİ ETTİRMEZ.</para>
/// </summary>
public class N11StockWithdrawer : ITransientDependency
{
    private readonly IRepository<SalesChannelTrN11Product, Guid> _repository;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IN11ProductRestClient _restClient;
    private readonly IN11TaskPoller _taskPoller;
    private readonly N11PushHistoryRecorder _historyRecorder;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<N11StockWithdrawer> _logger;

    public N11StockWithdrawer(
        IRepository<SalesChannelTrN11Product, Guid> repository,
        IRepository<SalesChannelTrN11, Guid> channelRepository,
        IN11ProductRestClient restClient,
        IN11TaskPoller taskPoller,
        N11PushHistoryRecorder historyRecorder,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<N11StockWithdrawer> logger)
    {
        _repository = repository;
        _channelRepository = channelRepository;
        _restClient = restClient;
        _taskPoller = taskPoller;
        _historyRecorder = historyRecorder;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    /// <summary>Kaydın bilinen tüm SKU'larına adet-0 gönderir (kanala hiç ulaşmamış kayıtta no-op — loglanır).
    /// Bayrak yazıldıktan SONRA, aynı UoW içinde çağrılır; istisna pasifleştirmeyi geri alır.</summary>
    public virtual async Task WithdrawStockAsync(SalesChannelTrN11Product entity)
    {
        if (entity.Skus.Count == 0)
        {
            // Hiç push edilmemiş kayıt: N11'de düşürülecek bir adet yok. Sessiz DEĞİL — iz log'da kalır ki
            // "pasifleştirdim ama N11'de hâlâ satışta" şikâyetinde ilk bakılacak yer belli olsun.
            _logger.LogInformation(
                "N11 adet-0 gönderimi atlandı: kanal ürünü {ChannelProductId} kanala hiç push edilmemiş (SKU yok).",
                entity.Id);
            return;
        }

        if (entity.PendingPushTaskId is { } pendingTaskId)
        {
            throw new BusinessException("TradeXpress:N11:Rest:PushPending").WithData("TaskId", pendingTaskId);
        }

        var channel = await _channelRepository.GetAsync(entity.SalesChannelId);
        var remoteReference = entity.N11ProductId?.ToString(CultureInfo.InvariantCulture);

        // Fiyat çifti son gönderilen değerde bırakılır (fiyatı hiç gönderilmemiş SKU'da null-null = stok-only
        // güncelleme, REST sözleşmesi buna açıkça izin verir). SyncStockAndPriceAsync'in "0 kurulabilir varyant"
        // dalıyla birebir aynı satır biçimi — iki yol farklı gövde üretseydi kanal ikisini farklı yorumlayabilirdi.
        var items = entity.Skus
            .Select(sku => new N11RestPriceStock(
                StockCode: sku.SellerStockCode,
                ListPrice: sku.LastSentOptionPrice,
                SalePrice: sku.LastSentOptionPrice,
                Quantity: 0,
                CurrencyType: null))
            .ToList();

        var failures = new List<string>();
        var queued = false;

        try
        {
            var submissions = await _restClient.UpdatePriceStockAsync(items, channel.AppKey, channel.AppSecret);
            if (submissions.Count == 0)
            {
                throw new BusinessException("TradeXpress:N11:Rest:NoSubmission");
            }

            // Gönder-sorgula modeli ResolveRestPushAsync ile aynı: makbuz REJECT diyebilir, task kuyrukta
            // kalabilir, işlense bile satırlar tek tek düşebilir. Beklemek YOK — kuyruğu işçi çözer.
            foreach (var submission in submissions)
            {
                if (N11TaskStates.Parse(submission.RawStatus) == N11TaskState.Rejected)
                {
                    failures.Add(submission.RawStatus);
                    continue;
                }

                var taskResult = await _taskPoller.QueryAsync(submission.TaskId, channel.AppKey, channel.AppSecret);

                if (taskResult.State == N11TaskState.InQueue)
                {
                    entity.MarkPushQueued(submission.TaskId, _clock.Now.ToUniversalTime());
                    queued = true;
                    continue;
                }

                if (taskResult.State != N11TaskState.Processed)
                {
                    failures.Add(taskResult.RejectReason ?? taskResult.State.ToString());
                    continue;
                }

                failures.AddRange(taskResult.Items
                    .Where(item => !item.Success)
                    .Select(item => $"{item.ItemCode}: {item.Reason}"));
            }
        }
        catch (Exception ex)
        {
            // Ağ/altyapı/makbuz hatası: denenen satırlar gerekçesiyle deftere geçer (2026-08-10 — "denendi" ile
            // "hiç denenmedi" ayırt edilebilsin), sonra istisna yukarı: çağıranın UoW'u pasifleştirmeyi geri alır.
            // Tipli hatada mesaj ABP'nin jenerik cümlesi olurdu — kod daha bilgilendiricidir.
            await RecordFailureSurvivingRollbackAsync(
                entity, items, remoteReference,
                ex is BusinessException { Code: { Length: > 0 } code } ? code : ex.Message);
            throw;
        }

        if (queued)
        {
            // Sonuç anı DEĞİL → defter satırı yok, LastSent* terfi etmez (kıyas tabanını yalnız başarı ilerletir).
            await _repository.UpdateAsync(entity, autoSave: true);
            _logger.LogInformation(
                "N11 adet-0 gönderimi kuyruğa alındı (kanal ürünü {ChannelProductId}, task {TaskId}); kuyruk işçisi çözecek.",
                entity.Id, entity.PendingPushTaskId);
            return;
        }

        if (failures.Count > 0)
        {
            // Kanalın kendi cümleleri deftere, tipli hata yukarı (fahiş fiyat bandı kendi koduyla ayrışır).
            await RecordFailureSurvivingRollbackAsync(entity, items, remoteReference, string.Join(" | ", failures));
            throw N11RestPushFailure.Build(failures);
        }

        // BAŞARI: dirty-check tabanı 0'a çekilir — yeniden aktifleşince gerçek adet "değişiklik" olarak görünür
        // ve normal senkron kendiliğinden geri yazar.
        foreach (var item in items)
        {
            entity.RecordStockPriceSync(item.StockCode, 0, item.SalePrice, version: null);
        }

        entity.MarkSynced(entity.N11ProductId, entity.SaleStatus, entity.ApprovalStatus, _clock.Now.ToUniversalTime());
        await _repository.UpdateAsync(entity, autoSave: true);

        await RecordAsync(entity, items, remoteReference, ChannelPushOutcome.Succeeded, errorMessage: null);
    }

    /// <summary>
    /// BAŞARISIZ delil satırı AYRI (requiresNew) transaction'da yazılır — dıştaki UoW rollback olsa bile KALIR.
    ///
    /// <para><b>Neden (2026-08-21 hakem bulgusu):</b> iki çağıran da <c>[UnitOfWork(isTransactional: true)]</c>
    /// kapsamında ve red/ağ hatasında istisna bilinçle yukarı fırlatılıyor (pasifleştirme geri alınsın diye).
    /// Delil aynı transaction'da yazılsaydı rollback onu da SİLERDİ: üretimde başarısız deneme deftere asla
    /// kalıcı geçmez, "denendi, kanal reddetti" ile "hiç denenmedi" yine ayırt edilemezdi — 2026-08-10
    /// "başarısız gönderim de yazılır" kuralının bu yoldaki sessiz ihlali. Delil satırı yalnız kimliklerle
    /// kurulur (dış entity state'ine bağımlılık yok), yeni transaction'da güvenle yazılır.</para>
    /// <para>Aynı desen <c>TrendyolListingWithdrawer</c>'a da uygulandı (2026-08-21, RecordFailureSurvivingRollbackAsync).</para>
    /// </summary>
    private async Task RecordFailureSurvivingRollbackAsync(
        SalesChannelTrN11Product entity,
        IReadOnlyCollection<N11RestPriceStock> items,
        string? remoteReference,
        string errorMessage)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await RecordAsync(entity, items, remoteReference, ChannelPushOutcome.Failed, errorMessage);
        await uow.CompleteAsync();
    }

    // Delil satırı: mevcut PriceStockSync yolu ne yazıyorsa o (ikinci bir defter biçimi İCAT EDİLMEZ) —
    // içerik bu yolda gönderilmediği için başlık/görsel null'dır, gönderilmeyeni yazmak yalan olurdu.
    private Task RecordAsync(
        SalesChannelTrN11Product entity,
        IReadOnlyCollection<N11RestPriceStock> items,
        string? remoteReference,
        ChannelPushOutcome outcome,
        string? errorMessage)
    {
        return _historyRecorder.RecordAsync(
            entity.CompanyId,
            entity.Id,
            N11ProductPushKind.PriceStockSync,
            N11PushHistoryRecorder.BuildPriceStockEntries(items),
            remoteReference,
            outcome,
            errorMessage);
    }
}
