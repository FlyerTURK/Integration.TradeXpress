using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Products.Rest;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// REST push'unun <b>KUYRUK</b> dalı — SOAP'ta karşılığı olmayan, tamamen yeni bir durum.
///
/// <para><b>Neden ayrı test sınıfı:</b> SOAP'ta <c>SaveProduct</c> sonucu anında dönerdi; REST'te yazma ucu
/// yalnız <c>taskId</c> + <c>IN_QUEUE</c> döndürebilir ve <b>HTTP 200 başarı ANLAMINA GELMEZ</b>. Bu üçüncü
/// durum (ne başarı ne hata) yanlış ele alınırsa iki felaket senaryosu doğar: kaydı "senkron" göstermek
/// (yalancı başarı) ya da hata sayıp kullanıcıyı tekrar push'a itmek (mükerrer listeleme).</para>
/// </summary>
public abstract class SalesChannelTrN11ProductQueuedPushTests<TStartupModule> : SalesChannelTrN11ProductPushTests<TStartupModule>
    where TStartupModule : Volo.Abp.Modularity.IAbpModule
{
    private readonly FakeN11TaskPoller _taskPoller;
    private readonly FakeN11ProductQueryClient _queryClient;

    protected SalesChannelTrN11ProductQueuedPushTests()
    {
        _taskPoller = GetRequiredService<FakeN11TaskPoller>();
        _queryClient = GetRequiredService<FakeN11ProductQueryClient>();
    }

    [Fact]
    public async Task Queued_push_is_not_reported_as_synced_and_remembers_the_task()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "QUEUE1", greenPrice: 150m, greenStock: 5);

            // Task kuyrukta kalsın: gerçek sonuç henüz yok.
            _taskPoller.Result = new N11TaskResult(N11TaskState.InQueue, Array.Empty<N11TaskItemResult>(), null);

            var pushed = await _appService.PushToN11Async(created.Id);

            // Kimlik SAKLANDI — akıbeti sonradan çözülebilsin diye.
            pushed.PendingPushTaskId.ShouldNotBeNullOrEmpty();
            // Ve BAŞARI SAYILMADI: hata da yazılmadı (task işlenmeye devam ediyor).
            pushed.LastError.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Resolving_a_still_queued_task_changes_nothing()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "QUEUE2", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.InQueue, Array.Empty<N11TaskItemResult>(), null);
            await _appService.PushToN11Async(created.Id);

            // Hâlâ kuyrukta → durum DEĞİŞMEMELİ. Bunu hata saymak kullanıcıyı boş yere telaşlandırırdı.
            var resolved = await _appService.ResolvePendingPushAsync(created.Id);

            resolved.PendingPushTaskId.ShouldNotBeNullOrEmpty();
            resolved.LastError.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Resolving_a_processed_task_closes_the_push()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "QUEUE3", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.InQueue, Array.Empty<N11TaskItemResult>(), null);
            await _appService.PushToN11Async(created.Id);

            // N11 task'ı işledi ve tüm satırlar başarılı.
            _taskPoller.Result = new N11TaskResult(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);

            var resolved = await _appService.ResolvePendingPushAsync(created.Id);

            // Bekleyen kimlik TEMİZLENDİ — dolu kalması "hâlâ bekliyor" anlamına gelirdi.
            resolved.PendingPushTaskId.ShouldBeNullOrEmpty();
            resolved.LastError.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Resolving_a_rejected_task_records_the_failure()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "QUEUE4", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.InQueue, Array.Empty<N11TaskItemResult>(), null);
            await _appService.PushToN11Async(created.Id);

            _taskPoller.Result = new N11TaskResult(
                N11TaskState.Rejected, Array.Empty<N11TaskItemResult>(), "Veri seti yüklenmedi");

            await Should.ThrowAsync<BusinessException>(() => _appService.ResolvePendingPushAsync(created.Id));
        }
    }

    /// <summary>Kısmi başarı NORMALDİR: task işlense bile SKU'lar tek tek başarısız olabilir. Bir satır bile
    /// düşerse push başarısız sayılmalı — yarım listelenmiş ürünü "senkron" göstermek eksikliği görünmez kılardı.</summary>
    [Fact]
    public async Task A_single_failed_sku_fails_the_whole_push()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "QUEUE5", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.InQueue, Array.Empty<N11TaskItemResult>(), null);
            await _appService.PushToN11Async(created.Id);

            _taskPoller.Result = new N11TaskResult(
                N11TaskState.Processed,
                new List<N11TaskItemResult>
                {
                    new("RED-1", true, null),
                    new("GREEN-1", false, "Zorunlu özellik eksik"),
                },
                null);

            await Should.ThrowAsync<BusinessException>(() => _appService.ResolvePendingPushAsync(created.Id));
        }
    }

    [Fact]
    public async Task Resolving_without_a_pending_task_is_rejected()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "QUEUE6", greenPrice: 150m, greenStock: 5);

            var ex = await Should.ThrowAsync<BusinessException>(
                () => _appService.ResolvePendingPushAsync(created.Id));

            ex.Code.ShouldBe("TradeXpress:N11:Rest:NoPendingTask");
        }
    }

    /// <summary>Fahiş fiyat bandı REDDİ kendi hata koduyla ayrılır: altın sıçradığında fiyat bandı aşılıp istek
    /// reddedilir ve ürün ESKİ (düşük) fiyatta satışta kalır. Genel "push başarısız" mesajına gömülürse
    /// operasyon farkı göremez — kuyumda bu doğrudan zarardır.</summary>
    [Fact]
    public async Task Excessive_price_rejection_gets_its_own_error_code()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "QUEUE7", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.InQueue, Array.Empty<N11TaskItemResult>(), null);
            await _appService.PushToN11Async(created.Id);

            _taskPoller.Result = new N11TaskResult(
                N11TaskState.Processed,
                new List<N11TaskItemResult>
                {
                    new("RED-1", false, "Bu ürün için girdiğiniz fiyatta fahiş fiyat düşüklüğü olduğundan..."),
                },
                null);

            var ex = await Should.ThrowAsync<BusinessException>(
                () => _appService.ResolvePendingPushAsync(created.Id));

            ex.Code.ShouldBe("TradeXpress:N11:Rest:PriceOutOfBand");
        }
    }

    // ── Fiyat/stok senkronu (REST price-stock-update) ────────────────────────────────────────────

    /// <summary>REST fiyat/stok guncellemesi SKU yu <b>bizim stockCode umuzla</b> adresler — SOAP in istedigi
    /// N11 SKU kimligine ihtiyac yok, bu yuzden on-okuma adimi da kalkti. Gercek senaryo: push edilir,
    /// sonra fiyat DEGISIR, senkron degisikligi gonderir.</summary>
    [Fact]
    public async Task Price_change_is_synced_addressing_skus_by_our_own_stock_code()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "SYNC1", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);
            await _appService.PushToN11Async(created.Id);

            // Fiyat degisir → senkron artik KIRLI goruyor.
            var fresh = await _appService.GetAsync(created.Id);
            var green = fresh.StockItems.Single(si => si.ProductVariantId is null);
            green.OverridePrice = 175m;
            var update = BuildUpdateDto(fresh);
            update.StockItems = fresh.StockItems;
            await _appService.UpdateAsync(created.Id, update);

            await _appService.SyncStockAndPriceAsync(created.Id);

            _restClient.PriceStockBatches.ShouldNotBeEmpty();
            var rows = _restClient.PriceStockBatches[^1];
            rows.ShouldContain(r => r.StockCode == "GREEN-1" && r.SalePrice == 175m);
            // listPrice >= salePrice ZORUNLU; ayri liste fiyati kavramimiz yok → esit gonderilir.
            rows.ShouldAllBe(r => r.ListPrice >= r.SalePrice);
        }
    }

    /// <summary>Basarili push zaten LastSent* i yazar → hemen ardindan yapilan senkron N11 e GITMEZ.
    /// Gereksiz yazma hem kotayi hem N11 in 60 sn kuralini zorlardi.</summary>
    [Fact]
    public async Task Sync_right_after_a_successful_push_sends_nothing()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "SYNC2", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);
            await _appService.PushToN11Async(created.Id);

            await _appService.SyncStockAndPriceAsync(created.Id);

            _restClient.PriceStockBatches.ShouldBeEmpty();
        }
    }

    /// <summary>Hic push edilmemis kayitta senkron reddedilir: dondurulmus stok kodu yoktur, adreslenecek
    /// bir SKU da yoktur.</summary>
    [Fact]
    public async Task Sync_before_any_push_is_rejected()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "SYNC3", greenPrice: 150m, greenStock: 5);

            var ex = await Should.ThrowAsync<BusinessException>(
                () => _appService.SyncStockAndPriceAsync(created.Id));

            ex.Code.ShouldBe("TradeXpress:N11:Product:NotPushedYet");
        }
    }

    // ── Push sonrasi REST geri okumasi ───────────────────────────────────────────────────────────

    /// <summary>REST yazma ucu urun kimligini DONDURMEZ → N11ProductId ancak product-query okumasindan gelir.
    /// Bu okuma olmadan alan sonsuza dek bos kalirdi.</summary>
    [Fact]
    public async Task Product_id_is_learned_from_the_rest_readback()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "READ1", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);
            _queryClient.Page = new N11RestProductPage(
                new[] { NewSummary(n11ProductId: 987654321L, categoryId: FakeN11CategoryClient.DefaultCategoryExternalId) },
                0, 1, 1L);

            var pushed = await _appService.PushToN11Async(created.Id);

            pushed.N11ProductId.ShouldBe(987654321L);
        }
    }

    /// <summary>N11 urunu BASKA bir kategoriye tasiyabilir (2026-07-07 karari) — kullanici bunu ogrenmeli.
    /// Geri okuma darlasti ama bu uyari KORUNDU, cunku en kritik olan bu.</summary>
    [Fact]
    public async Task Category_moved_by_n11_is_applied_and_warned()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "READ2", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);
            _queryClient.Page = new N11RestProductPage(
                new[] { NewSummary(n11ProductId: 1L, categoryId: "1219203") },   // N11 kategoriyi degistirdi
                0, 1, 1L);

            var pushed = await _appService.PushToN11Async(created.Id);

            pushed.CategoryExternalId.ShouldBe("1219203");
            pushed.SyncWarnings.ShouldNotBeEmpty();
        }
    }

    /// <summary>Okuma BOS donerse (push un hemen ardindan N11 de henuz gorunmuyor) akis saglikli ilerlemeli —
    /// push zaten basarili, geri okuma yalnizca zenginlestirme.</summary>
    [Fact]
    public async Task Empty_readback_does_not_break_a_successful_push()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "READ3", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);
            _queryClient.Page = new N11RestProductPage(Array.Empty<N11RestProductSummary>(), 0, 0, 0L);

            var pushed = await _appService.PushToN11Async(created.Id);

            pushed.LastError.ShouldBeNull();
            pushed.PendingPushTaskId.ShouldBeNullOrEmpty();
        }
    }

    private static N11RestProductSummary NewSummary(long n11ProductId, string categoryId)
    {
        return new N11RestProductSummary(
            n11ProductId, null, "RED-1", "Test", 100m, 100m, 10, "On_Sale", "Active", categoryId,
            Array.Empty<string>());
    }
}
