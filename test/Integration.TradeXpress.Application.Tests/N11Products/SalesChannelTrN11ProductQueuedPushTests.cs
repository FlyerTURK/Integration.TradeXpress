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
                    new("RED", true, null),
                    new("GREEN", false, "Zorunlu özellik eksik"),
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
                    new("RED", false, "Bu ürün için girdiğiniz fiyatta fahiş fiyat düşüklüğü olduğundan..."),
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
            rows.ShouldContain(r => r.StockCode == "GREEN" && r.SalePrice == 175m);
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

    /// <summary>
    /// <b>0 KURULABİLİR VARYANT → SATIŞ DURUR</b> (2026-08-05 Hakan kararı). Muadil ürünün stoğu tükenince
    /// <c>SubstitutionVariantMaterializer</c> hiç varyant üretmez ve push aday listesi BOŞ kalır.
    ///
    /// <para><b>Bu testin koruduğu şey:</b> o durumda eskiden <c>NoSyncableSku</c> fırlatılıyordu — yani N11'e
    /// HİÇBİR ŞEY gitmiyordu ve <b>son gönderilen adet orada CANLI kalıyordu</b>. Ürün karşılanamayacak sipariş
    /// almaya devam ediyordu (oversell → pazaryeri cezası). Hata "başarısız push" diye loglandığı için de
    /// sessiz kalıyordu. Artık bilinen tüm SKU'lara adet 0 gider.</para>
    ///
    /// <para>Fiyat sıfırlanmaz — amaç satışı durdurmak, listelemeyi kapatmak değil (N11'de "Out_Of_Stock").</para>
    /// </summary>
    [Fact]
    public async Task Sync_sends_zero_quantity_when_no_variant_can_be_built()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "SYNC0", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);
            await _appService.PushToN11Async(created.Id);

            var pushedStockCodes = _restClient.LastCreatedRows.Select(r => r.StockCode).ToList();
            pushedStockCodes.ShouldNotBeEmpty();
            _restClient.PriceStockBatches.Clear();   // push'un kendi yazımını sayma

            // Stoktan hiçbir kombinasyon kurulamaz hâle getir (materializer'ın 0 varyant üretmesinin karşılığı):
            // ERP varyantları pasifleşir (aday listesinden düşer) + N11-only satır kalkar.
            await MakeNoVariantBuildableAsync(created.Id);

            await _appService.SyncStockAndPriceAsync(created.Id);

            // Sessiz kalmak YASAK: gönderim OLMALI ve tüm satırlar 0 adet olmalı.
            var rows = _restClient.PriceStockBatches.ShouldHaveSingleItem();
            rows.ShouldNotBeEmpty();
            rows.ShouldAllBe(r => r.Quantity == 0);

            // Daha önce push edilmiş HER stok kodu sıfırlanmalı — biri atlanırsa o SKU satmaya devam eder.
            foreach (var stockCode in pushedStockCodes)
            {
                rows.ShouldContain(r => r.StockCode == stockCode);
            }

            // Fiyat korunur (satış durur, liste kapanmaz).
            rows.ShouldAllBe(r => r.SalePrice > 0m);
        }
    }

    /// <summary>Stok geri gelince senkron gerçek adedi KENDİLİĞİNDEN yazar — ayrı bir "yeniden aç" yolu yok
    /// (Hakan: "Açılsın tabi ki"). Simetri mevcut dirty-check'ten gelir; bu test onu kilitler.</summary>
    [Fact]
    public async Task Sync_restores_real_quantity_once_variants_are_buildable_again()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "SYNCR", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);
            await _appService.PushToN11Async(created.Id);

            var variantIds = await MakeNoVariantBuildableAsync(created.Id);
            await _appService.SyncStockAndPriceAsync(created.Id);   // → 0 gitti

            // Stok geri geldi: varyantlar yeniden kurulabilir hâle gelir.
            _restClient.PriceStockBatches.Clear();
            await SetVariantsSellableAsync(variantIds, sellable: true);

            await _appService.SyncStockAndPriceAsync(created.Id);

            // Ayrı bir "yeniden aç" çağrısı YOK — mevcut dirty-check gerçek adedi geri yazdı.
            var rows = _restClient.PriceStockBatches.ShouldHaveSingleItem();
            rows.ShouldContain(r => r.Quantity > 0);
        }
    }

    /// <summary>Push aday listesini BOŞALTIR — muadil materializer'ın stok bitince 0 varyant üretmesinin
    /// test karşılığı. İki şey birden gerekir: eksen nitelikleri temizlenir (yoksa reconcile N11-only
    /// kombinasyonu GERİ ÜRETİR) ve ERP varyantları pasifleşir (aday sorgusu <c>IsActive</c> filtreler).
    /// Dönüş: pasifleştirilen varyant kimlikleri — "stok geri geldi" senaryosu bunları geri açar.</summary>
    // "Stok geri geldi" senaryosunda varyantlara yazilan fiyat — orijinal tohum fiyatiyla ayni olmak zorunda
    // degil; test yalnizca "aday listesi yeniden doldu" sonucunu dogruluyor.
    private const decimal RestoredSalePrice = 150m;

    private async Task<List<Guid>> MakeNoVariantBuildableAsync(Guid channelProductId)
    {
        var dto = await _appService.GetAsync(channelProductId);
        var variantIds = dto.StockItems
            .Where(si => si.ProductVariantId is not null)
            .Select(si => si.ProductVariantId!.Value)
            .Distinct()
            .ToList();

        // N11-only satır DOĞRUDAN depodan silinir: DTO üzerinden temizlemek yetmiyor (reconcile kanal
        // niteliğindeki "Green" değerinden kombinasyonu geri üretiyor). ERP başlıkları KALIR — "stok geri
        // geldi" senaryosu varyantları yeniden aktif ederek onların üzerinden ilerler.
        var stockItemRepository =
            GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<SalesChannelTrN11ProductStockItem, Guid>>();
        await WithUnitOfWorkAsync(async () =>
        {
            await stockItemRepository.DeleteAsync(
                si => si.SalesChannelTrN11ProductId == channelProductId && si.ProductVariantId == null,
                autoSave: true);
        });

        await SetVariantsSellableAsync(variantIds, sellable: false);
        return variantIds;
    }

    /// <summary>Varyantları aday listesinden düşürür/geri getirir — kaldıraç <b>SATIŞ FİYATI</b>.
    ///
    /// <para><b>Neden IsActive DEĞİL (2026-08-08):</b> "ana varyant pasifleştirilemez" kuralı geldi
    /// (<c>EntityVariant.SetActive</c> fail-fast eder) ve bu helper ana varyantı da pasifleştiriyordu.
    /// Kural testi kırdığı için testi gevşetmek YASAK — bunun yerine AYNI sonucu üreten MEŞRU bir kaldıraca
    /// geçildi: aday sorgusu fiyatsız varyantı zaten eliyor (<c>SalePrice is not null</c> süzgeci, kapıdan
    /// da ÖNCE). Testin iddiaları birebir aynı kaldı; yalnız senaryonun kurulma yolu değişti.</para>
    ///
    /// <para>Gerçek hayattaki karşılığı da meşru: fiyatı çözülemeyen varyant push adayı olamaz.</para></summary>
    private async Task SetVariantsSellableAsync(List<Guid> variantIds, bool sellable)
    {
        var detailRepository =
            GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<Products.ProductVariantDetail, Guid>>();
        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var variantId in variantIds)
            {
                var detail = await detailRepository.FindAsync(d => d.EntityVariantId == variantId);
                if (detail is null)
                {
                    continue;
                }

                detail.SetSalePrice(sellable ? RestoredSalePrice : null, detail.SalePriceCurrencyUnitId);
                await detailRepository.UpdateAsync(detail, autoSave: true);
            }
        });
    }

    /// <summary>
    /// <b>PUSH KAPISI</b> (2026-08-05 Hakan kararı: *"kararsız reçeteli bir ürün kesinlikle satışa girmemeli
    /// — düşünsene pırlantayı bedava sattığımız senaryoyu"*).
    ///
    /// <para>Varyantın onayı düştüğünde (reçete değişti / emtia gitti / hiç onaylanmadı) o varyant push aday
    /// listesine GİRMEZ. Kapı fiyatlamadan ÖNCE olduğu için <b>elle girilen özel fiyat da kararsızlığı
    /// ÖRTEMEZ</b> — eski davranışta <c>OverridePrice ?? türetilmiş</c> zinciri yüzünden örtebiliyordu.</para>
    ///
    /// <para>Bu test kapının KENDİSİNİ kilitler: kapı kalkarsa doğrulanmamış varyant sessizce push edilir ve
    /// arıza ancak pazaryerinde yanlış fiyat olarak görünür.</para>
    /// </summary>
    [Fact]
    public async Task Unverified_variant_never_becomes_a_push_row_even_with_an_override_price()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "GATE1", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);
            await _appService.PushToN11Async(created.Id);

            var erpCodesWhileVerified = _restClient.LastCreatedRows.Select(r => r.StockCode).ToList();
            erpCodesWhileVerified.Count.ShouldBeGreaterThan(1);   // ERP varyantları + N11-only satır

            // Varyantların onayı düşer (üretimde: reçete değişimi ya da emtia pasifleşmesi).
            await UnverifyErpVariantsAsync(created.Id);

            _restClient.CreatedBatches.Clear();
            await _appService.PushToN11Async(created.Id);

            // ERP-backed satırlar DÜŞTÜ; yalnız N11-only satır (kendi override fiyatıyla) kaldı.
            var afterCodes = _restClient.LastCreatedRows.Select(r => r.StockCode).ToList();
            afterCodes.Count.ShouldBeLessThan(erpCodesWhileVerified.Count);
        }
    }

    /// <summary>Tüm ERP varyantlarının onayını düşürür — üretimdeki "reçete değişti / emtia pasifleşti"
    /// sonucunun test karşılığı.</summary>
    private async Task UnverifyErpVariantsAsync(Guid channelProductId)
    {
        var dto = await _appService.GetAsync(channelProductId);
        var variantIds = dto.StockItems
            .Where(si => si.ProductVariantId is not null)
            .Select(si => si.ProductVariantId!.Value)
            .Distinct()
            .ToList();

        var details = GetRequiredService<Volo.Abp.Domain.Repositories.IRepository<Products.ProductVariantDetail, Guid>>();
        await WithUnitOfWorkAsync(async () =>
        {
            var all = await details.GetListAsync(d => variantIds.Contains(d.EntityVariantId));
            foreach (var detail in all)
            {
                detail.Close();   // Ready DIŞINDA herhangi bir durum kapıyı kapatır
                await details.UpdateAsync(detail, autoSave: true);
            }
        });
    }

    /// <summary>
    /// <b>PUSH GEÇMİŞİ APPEND-ONLY</b> (2026-08-05 Hakan kararı).
    ///
    /// <para><b>Neden bu test var:</b> N11 ürünün her versiyonunu FOTOĞRAFIYLA delil olarak saklıyor
    /// ("23/07'de şu varyantı şu fiyata satmıştın") ve aynı üründe fotoğraf değiştiği için iki farklı
    /// siparişte farklı resim göründüğü kullanıcı tarafından YAŞANDI. Bizde <c>LastSent*</c> her push'ta
    /// ÜZERİNE yazılıyordu — yani karşı taraf tarihli kayıt gösterirken biz aynı cümleyi kuramıyorduk.</para>
    ///
    /// <para>Test iki şeyi kilitler: (1) kayıt YAZILIYOR, (2) ikinci push öncekini EZMİYOR — üzerine yazılan
    /// bir delil delil değildir.</para>
    /// </summary>
    [Fact]
    public async Task Push_history_is_appended_and_never_overwritten()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "HIST1", greenPrice: 150m, greenStock: 5);
            _taskPoller.Result = new N11TaskResult(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);

            await _appService.PushToN11Async(created.Id);
            var afterFirst = await ReadHistoryAsync(created.Id);
            afterFirst.Count.ShouldBeGreaterThan(0);
            afterFirst.ShouldAllBe(h => h.PushKind == N11ProductPushKind.FullPush);
            afterFirst.ShouldAllBe(h => h.PushedAtUtc != default);

            // Fiyat değişir → ikinci gönderim.
            var fresh = await _appService.GetAsync(created.Id);
            var green = fresh.StockItems.Single(si => si.ProductVariantId is null);
            green.OverridePrice = 175m;
            var update = BuildUpdateDto(fresh);
            update.StockItems = fresh.StockItems;
            await _appService.UpdateAsync(created.Id, update);
            await _appService.SyncStockAndPriceAsync(created.Id);

            var afterSecond = await ReadHistoryAsync(created.Id);

            // ASIL KURAL: eski kayıt DURUYOR, yenisi EKLENDİ.
            afterSecond.Count.ShouldBeGreaterThan(afterFirst.Count);

            // İki fiyat da geçmişte YAN YANA — "o gün şu fiyattaydı" ancak böyle söylenebilir.
            var prices = afterSecond.Select(h => h.SalePrice).ToList();
            prices.ShouldContain(150m);
            prices.ShouldContain(175m);

            // Senkron içerik göndermez → başlık/görsel null (gönderilmeyeni yazmak yalan olurdu).
            afterSecond.Where(h => h.PushKind == N11ProductPushKind.PriceStockSync)
                .ShouldAllBe(h => h.Title == null && h.Images == null);
        }
    }

    private Task<List<SalesChannelTrN11ProductPushHistory>> ReadHistoryAsync(Guid channelProductId)
    {
        var repo = GetRequiredService<
            Volo.Abp.Domain.Repositories.IRepository<SalesChannelTrN11ProductPushHistory, Guid>>();

        return WithUnitOfWorkAsync(async () =>
        {
            var rows = await repo.GetListAsync(h => h.SalesChannelTrN11ProductId == channelProductId);
            return rows.OrderBy(h => h.PushedAtUtc).ToList();
        });
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
            n11ProductId, null, "RED", "Test", 100m, 100m, 10, "On_Sale", "Active", categoryId,
            Array.Empty<string>());
    }
}
