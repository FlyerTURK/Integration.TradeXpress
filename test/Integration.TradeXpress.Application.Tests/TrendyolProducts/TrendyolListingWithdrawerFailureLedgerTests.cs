using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannelProducts;
using Integration.TradeXpress.SalesChannels;
using Microsoft.Extensions.Logging;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// BAŞARISIZ TRENDYOL GERİ-ÇEKME DELİLİ ROLLBACK'TEN SAĞ ÇIKAR (2026-08-21 hakem bulgusu —
/// <c>N11StockWithdrawer</c> ile aynı gün düzeltilen aynı kusur): silme/arşiv yolunda kanal reddi istisnayla
/// yukarı çıkar ve çağıranın transactional UoW'u DB işini GERİ ALIR (sınıf sözleşmesinin kendisi); Failed delil
/// satırı AYNI transaction'da yazılsaydı rollback onu da silerdi — 2026-08-10 "başarısız gönderim de yazılır"
/// kuralının bu yoldaki sessiz ihlali. Düzeltme: delil, KENDİ requiresNew transactional UoW'unda yazılıp
/// istisna yukarı çıkmadan ÖNCE complete edilir (<c>TrendyolListingWithdrawer.RecordFailureSurvivingRollbackAsync</c>).
///
/// <para><b>⚠ Test, SATIRIN DB'DE KALDIĞINI değil MEKANİZMAYI kilitler.</b> Test ortamı paylaşımlı TEK
/// bağlantılı in-memory Sqlite'tır ve Microsoft.Data.Sqlite aynı bağlantıda ikinci transaction'ı TAŞIYAMAZ
/// ("SqliteConnection does not support nested transactions") — dış transaction açıkken içteki requiresNew
/// transaction'ın gerçek DB yazımı BU ORTAMDA GÖZLEMLENEMEZ (recorder'ın "kayıt push'u düşürmez" yutması hatayı
/// sessizce loga düşürür; üretim SQL Server'da her DbContext KENDİ bağlantısını aldığından yazım gerçekleşir —
/// delil tablosunda FK da yok, kilit beklemesi doğmaz). İddia bu yüzden kayıt ANINDAKİ ambient UoW'un kimliği
/// üzerinden kurulur; sabotaj ağı yine tamdır:
/// ① <c>requiresNew</c> kaldırılırsa child UoW ambient'i DEĞİŞTİRMEZ → kayıt anındaki UoW dıştaki çıkar → kırmızı;
/// ② <c>isTransactional</c> kaldırılırsa → kırmızı; ③ <c>CompleteAsync</c> unutulursa <c>OnCompleted</c> hiç
/// ateşlenmez → kırmızı; ④ delil çağrısı kaldırılır/istisnadan sonraya taşınırsa spy hiç çağrılmaz → kırmızı.</para>
/// </summary>
public abstract class TrendyolListingWithdrawerFailureLedgerTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly FakeTrendyolProductClient _fakeClient;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _recordRepository;
    private readonly IUnitOfWorkManager _uowManager;
    private readonly ICurrentCompany _currentCompany;
    private readonly RecordingPushHistoryRecorder _spy;
    private readonly TrendyolListingWithdrawer _withdrawer;

    protected TrendyolListingWithdrawerFailureLedgerTests()
    {
        _fakeClient = GetRequiredService<FakeTrendyolProductClient>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrTrendyol, Guid>>();
        _recordRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProduct, Guid>>();
        _uowManager = GetRequiredService<IUnitOfWorkManager>();
        _currentCompany = GetRequiredService<ICurrentCompany>();

        // Withdrawer ELLE kurulur: konudaki sınıfın kendisi test edilir, defter yazıcısı ise UoW-kimliği
        // yakalayan spy ile değiştirilir (container'da global replace, kardeş testlerin gerçek defter
        // iddialarını sessizce boşa çıkarırdı).
        _spy = new RecordingPushHistoryRecorder(
            GetRequiredService<IRepository<SalesChannelTrTrendyolProductPushHistory, Guid>>(),
            GetRequiredService<IRepository<Media, Guid>>(),
            GetRequiredService<IClock>(),
            GetRequiredService<ILogger<TrendyolPushHistoryRecorder>>(),
            _uowManager);
        _withdrawer = new TrendyolListingWithdrawer(_fakeClient, _channelRepository, _spy, _uowManager);
    }

    /// <summary>① Silme yolu: kanal reddinde DB işi (soft-delete) GERİ DÖNER ama Failed delil yazımı dıştaki
    /// UoW'da DEĞİL, yeni + transactional + complete edilmiş bir UoW'da yapılır — rollback delili silemez.</summary>
    [Fact]
    public async Task A_rejected_delete_rolls_back_the_work_but_records_the_failure_in_its_own_new_transaction()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var recordId = await SeedListedRecordAsync(companyId, "WDR1", "BR-WDR-1");
            _fakeClient.RejectDeletes = true;
            try
            {
                Guid outerUowId;
                using (var outer = _uowManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
                {
                    outerUowId = outer.Id;

                    // Remover'ın gerçek sırası: ÖNCE DB (soft-delete), SONRA kanal — red ikisini birden geri alır.
                    var record = await _recordRepository.GetAsync(recordId);
                    await _recordRepository.DeleteAsync(record, autoSave: true);

                    var ex = await Should.ThrowAsync<BusinessException>(() => _withdrawer.WithdrawAsync(record));
                    ex.Code.ShouldBe("TradeXpress:Trendyol:Product:DeleteFailed");
                    // Complete YOK — istisna yolu; dispose rollback eder (çağıran app service'te de aynı akıbet).
                }

                // DB işi geri döndü: soft-delete rollback oldu, kayıt yerinde.
                (await WithUnitOfWorkAsync(async () => await _recordRepository.FindAsync(recordId))).ShouldNotBeNull();

                // Delil girişimi: Failed + kanalın hata kodu + doğru tür/barkod...
                var capture = _spy.Captures.ShouldHaveSingleItem();
                capture.Outcome.ShouldBe(ChannelPushOutcome.Failed);
                capture.Kind.ShouldBe(TrendyolProductPushKind.Delete);
                capture.Barcodes.ShouldBe(new[] { "BR-WDR-1" });
                capture.ErrorMessage.ShouldBe("TradeXpress:Trendyol:Product:DeleteFailed");

                // ...ve YENİ, transactional, COMPLETE edilmiş bir UoW'da (sabotaj ağının üç teli).
                capture.UowId.ShouldNotBe(outerUowId,
                    "Delil dıştaki UoW'da yazılırsa rollback onu da siler — requiresNew şart.");
                capture.UowIsTransactional.ShouldBeTrue();
                _spy.CompletedUowIds.ShouldContain(capture.UowId,
                    "Complete edilmeyen UoW hiçbir şeyi kalıcılaştırmaz.");
            }
            finally
            {
                _fakeClient.RejectDeletes = false;
            }
        }
    }

    /// <summary>② Arşiv/bayrak yolu aynı SendAsync gövdesinden geçer — kanal reddinde Failed delil yine dıştaki
    /// UoW'un DIŞINDA yazılır. (Yollar ileride ayrışırsa yalnız birinin düzeltilmesi burada kırmızı yakalanır.)</summary>
    [Fact]
    public async Task A_rejected_archive_change_records_the_failure_outside_the_caller_transaction()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var recordId = await SeedListedRecordAsync(companyId, "WDR2", "BR-WDR-2");
            _fakeClient.RejectArchiveChanges = true;
            try
            {
                Guid outerUowId;
                using (var outer = _uowManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true))
                {
                    outerUowId = outer.Id;
                    var record = await _recordRepository.GetAsync(recordId);

                    var ex = await Should.ThrowAsync<BusinessException>(
                        () => _withdrawer.SetArchivedAsync(record, archived: true));
                    ex.Code.ShouldBe("TradeXpress:Trendyol:Product:ArchiveFailed");
                }

                var capture = _spy.Captures.ShouldHaveSingleItem();
                capture.Outcome.ShouldBe(ChannelPushOutcome.Failed);
                capture.Kind.ShouldBe(TrendyolProductPushKind.Archive);
                capture.ErrorMessage.ShouldBe("TradeXpress:Trendyol:Product:ArchiveFailed");
                capture.UowId.ShouldNotBe(outerUowId);
                capture.UowIsTransactional.ShouldBeTrue();
                _spy.CompletedUowIds.ShouldContain(capture.UowId);
            }
            finally
            {
                _fakeClient.RejectArchiveChanges = false;
            }
        }
    }

    // ── Seed ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Kanala ULAŞMIŞ kayıt: barkodu bilinen SKU'su olan Trendyol kanal ürünü. Ürün satırı açılmaz —
    /// withdrawer ürünü okumaz, delil id-only'dir (FK yok); test konusu dışındaki kurulum gürültüsü eklenmez.</summary>
    private async Task<Guid> SeedListedRecordAsync(Guid companyId, string suffix, string barcode)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var channel = await _channelRepository.InsertAsync(
                new SalesChannelTrTrendyol(companyId, $"TY-{suffix}", $"Trendyol Kanal {suffix}", "seller-1", "api-key", "api-secret"),
                autoSave: true);
            var record = new SalesChannelTrTrendyolProduct(
                companyId, channel.Id, Guid.NewGuid(), productMainId: "WDR-" + suffix, sequenceNo: 1, categoryId: "411", brandId: "82");
            record.UpsertImportedSku(Guid.NewGuid(), barcode, "STK-" + suffix, remoteContentId: 1);
            await _recordRepository.InsertAsync(record, autoSave: true);
            return record.Id;
        });
    }

    // ── Spy ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Delil yazımını DB'ye DOKUNMADAN yakalar: Sqlite paylaşımlı bağlantı ikinci transaction'ı
    /// taşıyamadığından gerçek yazım bu ortamda gözlemlenemez; iddia kayıt ANINDAKİ ambient UoW'un kimliği ve
    /// akıbeti (<c>OnCompleted</c>) üzerinden kurulur.</summary>
    private sealed class RecordingPushHistoryRecorder : TrendyolPushHistoryRecorder
    {
        private readonly IUnitOfWorkManager _uowManager;

        public RecordingPushHistoryRecorder(
            IRepository<SalesChannelTrTrendyolProductPushHistory, Guid> repository,
            IRepository<Media, Guid> mediaRepository,
            IClock clock,
            ILogger<TrendyolPushHistoryRecorder> logger,
            IUnitOfWorkManager uowManager)
            : base(repository, mediaRepository, clock, logger)
        {
            _uowManager = uowManager;
        }

        public List<RecordCapture> Captures { get; } = new();

        /// <summary>Kayıt anındaki UoW'lardan gerçekten COMPLETE edilenler — "CompleteAsync unutuldu" sabotajının ağı.</summary>
        public List<Guid> CompletedUowIds { get; } = new();

        public override Task RecordAsync(
            Guid companyId,
            Guid channelProductId,
            TrendyolProductPushKind pushKind,
            IReadOnlyCollection<TrendyolPushHistoryEntry> entries,
            string? batchRequestId,
            ChannelPushOutcome outcome,
            string? errorMessage = null)
        {
            var current = _uowManager.Current.ShouldNotBeNull("Delil yazımı bir UoW kapsamında olmalı.");
            current.OnCompleted(() =>
            {
                CompletedUowIds.Add(current.Id);
                return Task.CompletedTask;
            });

            Captures.Add(new RecordCapture(
                pushKind,
                outcome,
                errorMessage,
                entries.Select(e => e.Barcode).ToList(),
                current.Id,
                current.Options.IsTransactional));
            return Task.CompletedTask;
        }
    }

    private sealed record RecordCapture(
        TrendyolProductPushKind Kind,
        ChannelPushOutcome Outcome,
        string? ErrorMessage,
        IReadOnlyList<string> Barcodes,
        Guid UowId,
        bool UowIsTransactional);
}
