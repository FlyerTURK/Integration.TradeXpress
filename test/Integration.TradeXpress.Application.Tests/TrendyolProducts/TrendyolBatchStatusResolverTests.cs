using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// TRENDYOL BATCH DURUM ÇÖZÜCÜSÜ — işçi mantığının yaşadığı sınıf (2026-08-19 haritası, öncelik #2).
///
/// <para><b>Neden bu testler:</b> işçi canlıda fiilen hiç çalışmıyordu ve bunu hiçbir test yakalamamıştı — işçi kodu
/// doğrudan app service'i çağırıyordu ve testler app service'i zaten şirket bağlamı ALTINDA koşturuyordu. Bu sınıf
/// çözücüyü tam da işçinin koştuğu şartta çağırır: <b>ambient şirket YOK, ambient kullanıcı YOK</b>. Eski işçi kodu bu
/// şartta "bekleyen" bulsa bile <c>GetOwnedAsync</c>'in şirket guard'ına takılırdı (WorkingCompanyRequired).</para>
///
/// <para>İkinci pin: 24 saatten eski ve cevap alınamayan batch <b>PROCESSING'ten çıkar</b> — eski yol yalnız hata
/// metni yazıyor, çifte-batch koruması kilidi sonsuza kadar tutuyordu.</para>
/// </summary>
public abstract class TrendyolBatchStatusResolverTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly TrendyolBatchStatusResolver _resolver;
    private readonly ISalesChannelTrTrendyolProductAppService _appService;
    private readonly TrendyolChannelProductTestSeeder _seeder;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _channelProductRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly FakeTrendyolProductClient _client;

    protected TrendyolBatchStatusResolverTests()
    {
        _resolver = GetRequiredService<TrendyolBatchStatusResolver>();
        _appService = GetRequiredService<ISalesChannelTrTrendyolProductAppService>();
        _seeder = GetRequiredService<TrendyolChannelProductTestSeeder>();
        _channelProductRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProduct, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _client = GetRequiredService<FakeTrendyolProductClient>();
        _client.AllowPriceInventoryWrites = true;
    }

    /// <summary>İşçi şartı: şirket yok, kullanıcı yok. Çözücü kaydın KENDİ şirketini ve tenant admin'ini kurup
    /// batch'i finalize etmeli — COMPLETED → LastSent terfi eder, Status PROCESSING'ten çıkar.</summary>
    [Fact]
    public async Task Resolver_finalizes_a_processing_batch_without_ambient_company_or_user()
    {
        var companyId = Guid.NewGuid();
        Guid recordId;
        using (_currentCompany.Change(companyId))
        {
            var created = await _seeder.SeedAsync(companyId, "TYWRK1", verify: true, seedSkus: true);
            await _appService.SyncStockAndPriceAsync(created.Id);
            recordId = created.Id;
        }

        (await LoadAsync(recordId)).Status.ShouldBe(TrendyolProductConsts.ProcessingBatchStatus);
        _client.NextBatchStatus = new TrendyolBatchStatus("COMPLETED", 2, 0, null);

        TrendyolBatchResolveReport report;
        using (_currentCompany.Change(null))
        using (_principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity())))
        {
            report = await _resolver.ResolvePendingAsync();
        }

        report.SkippedNoAdmin.ShouldBeFalse("tenant admin bulunmalı — bulunamadıysa işçi yine sessiz kalır");
        report.Pending.ShouldBeGreaterThanOrEqualTo(1);
        report.Failed.ShouldBe(0);

        var resolved = await LoadAsync(recordId);
        resolved.Status.ShouldBe("COMPLETED");
        resolved.Skus.Select(s => s.LastSentQuantity).ShouldBe(new int?[] { 10, 20 }, ignoreOrder: true);
    }

    /// <summary>Cevap alınamayan ve 24 saati aşan batch BAYAT işaretlenir: Status artık PROCESSING değil,
    /// bekleyen SKU değerleri atıldı, BatchRequestId KORUNDU ve hafif senkron yeniden kabul edilir
    /// (BatchInProgress kilidi açıldı). Eski yolun açtığını SANDIĞI kilit tam da buydu.</summary>
    [Fact]
    public async Task A_stale_unanswered_batch_is_released_and_sync_is_accepted_again()
    {
        var companyId = Guid.NewGuid();
        Guid recordId;
        using (_currentCompany.Change(companyId))
        {
            var created = await _seeder.SeedAsync(companyId, "TYWRK2", verify: true, seedSkus: true);
            recordId = created.Id;

            await WithUnitOfWorkAsync(async () =>
            {
                var entity = await _channelProductRepository.GetAsync(recordId);
                entity.MarkSubmitted("OLD-BATCH", "ProductV2OnBoarding", DateTime.UtcNow.AddHours(-25));
                await _channelProductRepository.UpdateAsync(entity, autoSave: true);
            });
        }

        // Trendyol cevap vermiyor (sahte istemci batch durumunu bilmiyor → fırlatır).
        _client.NextBatchStatus = null;

        TrendyolBatchResolveReport report;
        using (_currentCompany.Change(null))
        using (_principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity())))
        {
            report = await _resolver.ResolvePendingAsync();
        }

        report.MarkedStale.ShouldBeGreaterThanOrEqualTo(1);

        var stale = await LoadAsync(recordId);
        stale.Status.ShouldBe(TrendyolProductConsts.StaleBatchStatus);
        stale.LastError.ShouldBe(TrendyolProductConsts.StaleBatchError);
        stale.BatchRequestId.ShouldBe("OLD-BATCH");   // elle "Durumu Yenile" hâlâ mümkün
        stale.Skus.ShouldAllBe(s => s.PendingSentQuantity == null);

        // Kilit AÇILDI: hafif senkron BatchInProgress demeden Trendyol'a gider.
        using (_currentCompany.Change(companyId))
        {
            var synced = await _appService.SyncStockAndPriceAsync(recordId);
            synced.Status.ShouldBe(TrendyolProductConsts.ProcessingBatchStatus);
            synced.BatchRequestId.ShouldNotBe("OLD-BATCH");
        }
    }

    /// <summary>Henüz 24 saati doldurmamış ama cevap alınamayan batch BAYATLANMAZ — beklemeye devam.</summary>
    [Fact]
    public async Task A_fresh_unanswered_batch_keeps_waiting()
    {
        var companyId = Guid.NewGuid();
        Guid recordId;
        using (_currentCompany.Change(companyId))
        {
            var created = await _seeder.SeedAsync(companyId, "TYWRK3", verify: true, seedSkus: true);
            await _appService.SyncStockAndPriceAsync(created.Id);
            recordId = created.Id;
        }

        _client.NextBatchStatus = null;

        using (_currentCompany.Change(null))
        using (_principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity())))
        {
            var report = await _resolver.ResolvePendingAsync();
            report.MarkedStale.ShouldBe(0);
        }

        (await LoadAsync(recordId)).Status.ShouldBe(TrendyolProductConsts.ProcessingBatchStatus);
    }

    private Task<SalesChannelTrTrendyolProduct> LoadAsync(Guid id)
    {
        return WithUnitOfWorkAsync(() => _channelProductRepository.GetAsync(id));
    }
}
