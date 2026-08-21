using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannelProducts;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// PASİFLEŞME ANINDA N11'E ADET-0 GİDER (2026-08-21 Hakan kararı: <i>"Tabi ki isactive false ise derhal 0 stok
/// olmalı"</i>) — <c>N11StockWithdrawer</c> + iki tetik noktası (form geçişi · ürün cascade'i) kilitlenir.
///
/// <para><b>Neden gerekliydi:</b> <c>PassiveNoSync</c> guard'ı + stok tetiği süzgeci (PassiveSyncTests) yalnız
/// YENİ yazımı keser; son gönderilen adet N11'de canlı kalır ve o listelemeden sipariş gelebilirdi. Beş kilit:
/// ① form geçişi (aktif→pasif) bilinen TÜM SKU'lara adet-0 gönderir, FİYAT KORUNUR (satış durur, listeleme
/// kapanmaz — "Out_Of_Stock"); ② ürün pasifleşme cascade'i aynı gönderimi yapar, ürün yeniden aktifleşince kanal
/// AÇILMAZ (tek yönlü baskı); ③ kanala hiç ulaşmamış (SKU'suz) kayıtta gönderim YAPILMAZ; ④ N11 reddederse
/// pasifleştirme GERİ DÖNER (aynı transaction — biz ile kanal farklı şey söyleyemez); ⑤ yeniden aktifleşme ANINDA
/// senkron tetikler ve dirty-check (LastSent=0 ≠ gerçek) gerçek adedi geri yazar — ayrı "yeniden aç" yolu yok.</para>
///
/// <para><b>Sabotaj değeri:</b> her iddia trafiğe/dirty-tabanına bağlıdır — adet-0 çağrısı kaldırılırsa batch
/// sayısı artmaz (①/② kırmızı), LastSent 0'a çekilmezse geri dönüş senkronu "değişiklik yok" der (⑤ kırmızı),
/// kuyruk çözümünün pasif dalı kaldırılırsa plan dondurması gerçek adetleri "gönderildi" yazar (⑥ kırmızı).</para>
/// </summary>
public abstract class SalesChannelTrN11ProductZeroStockOnDeactivateTests<TStartupModule>
    : SalesChannelTrN11ProductPushTests<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly FakeN11TaskPoller _taskPoller;
    private readonly IRepository<SalesChannelTrN11ProductPushHistory, Guid> _historyRepository;

    protected SalesChannelTrN11ProductZeroStockOnDeactivateTests()
    {
        _taskPoller = GetRequiredService<FakeN11TaskPoller>();
        _historyRepository = GetRequiredService<IRepository<SalesChannelTrN11ProductPushHistory, Guid>>();
    }

    /// <summary>① Form geçişi: aktif→pasif TÜM bilinen SKU'lara adet-0 gönderir; fiyat SON GÖNDERİLEN değerde
    /// kalır (amaç satışı durdurmak, listelemeyi kapatmak değil). LastSent tabanı 0'a çekilir ki geri dönüş
    /// senkronu gerçek adedi "değişiklik" olarak görsün; defterde PriceStockSync/Succeeded delil satırları düşer.</summary>
    [Fact]
    public async Task Deactivating_via_the_form_sends_zero_quantity_for_all_skus_and_keeps_the_price()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "ZERO1", greenPrice: 150m, greenStock: 8);
            await _appService.PushToN11Async(created.Id);
            var batchesBefore = _restClient.PriceStockBatches.Count;

            await MutateAsync(created.Id, u => u.IsActive = false);

            // Adet-0 batch'i GİTTİ: 3 SKU (Red/Blue ERP + Green N11-only), hepsinde adet 0, fiyatlar korunmuş.
            _restClient.PriceStockBatches.Count.ShouldBe(batchesBefore + 1);
            var batch = _restClient.PriceStockBatches[^1];
            batch.Count.ShouldBe(3);
            batch.ShouldAllBe(r => r.Quantity == 0);
            batch.Select(r => r.SalePrice).OrderBy(p => p).ShouldBe(new decimal?[] { 100m, 100m, 150m });

            // Dirty-check tabanı 0'a çekildi, fiyat tabanı korundu — geri dönüşün tek mekanizması budur.
            var reloaded = await _appService.GetAsync(created.Id);
            reloaded.IsActive.ShouldBeFalse();
            reloaded.Skus.Count.ShouldBe(3);
            reloaded.Skus.ShouldAllBe(s => s.LastSentQuantity == 0);
            reloaded.Skus.Select(s => s.LastSentOptionPrice).OrderBy(p => p).ShouldBe(new decimal?[] { 100m, 100m, 150m });

            // Delil: adet-0 da bir PriceStockSync gönderimidir — sonuç anında Succeeded satırları düşer.
            var ledger = await WithUnitOfWorkAsync(async () => await _historyRepository.GetListAsync(
                h => h.SalesChannelTrN11ProductId == created.Id && h.PushKind == N11ProductPushKind.PriceStockSync));
            ledger.Count.ShouldBe(3);
            ledger.ShouldAllBe(h => h.Outcome == ChannelPushOutcome.Succeeded && h.Quantity == 0);
        }
    }

    /// <summary>② Ürün cascade'i: ana ürün pasifleşince N11 kanal ürünü pasifleşir VE adet-0 gider; ürün yeniden
    /// aktifleşince kanal ürünü pasif KALIR ve N11'e yeni istek gitmez (tek yönlü baskı — kanal kanal insan kararı).</summary>
    [Fact]
    public async Task Deactivating_the_product_sends_zero_quantity_but_reactivating_does_not_reopen()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "ZERO2", greenPrice: 150m, greenStock: 8);
            await _appService.PushToN11Async(created.Id);
            var products = GetRequiredService<IProductAppService>();
            var mapper = GetRequiredService<Volo.Abp.ObjectMapping.IObjectMapper>();
            var batchesBefore = _restClient.PriceStockBatches.Count;

            // Ürün PASİF. (Ürün kategorisi güncellemede ZORUNLU — seed kategorisiz açıldığından burada verilir.)
            var input = mapper.Map<ProductGetDto, ProductUpdateDto>(await products.GetAsync(created.ProductId));
            input.ProductCategoryId = await CreateTestProductCategoryAsync("N11 Adet-0 Cascade Kategori");
            input.IsActive = false;
            await products.UpdateAsync(created.ProductId, input);

            (await _appService.GetAsync(created.Id)).IsActive.ShouldBeFalse();
            _restClient.PriceStockBatches.Count.ShouldBe(batchesBefore + 1);
            _restClient.PriceStockBatches[^1].ShouldAllBe(r => r.Quantity == 0);

            // Ürün yeniden AKTİF — ve BİLE BİLE BAYAT grafla (2026-08-21 hakem bulgusunun ağı): pasifleştirme
            // ÖNCESİ alınan dto'nun kanal grafı hâlâ IsActive=true taşıyor. Koruma (SaveChannelProductsGraphAsync:
            // IsActive ürün grafından yazılmaz, sunucudaki değer korunur) olmasaydı bu kayıt kanalı sessizce
            // yeniden açar ve aktifleşme senkronu GERÇEK adetleri N11'e geri yazardı — kapatılmak istenen
            // oversell deliğinin ta kendisi. İlk sürüm bu testi TAZE dto'yla deliğin etrafından dolaştırıyordu;
            // şimdi tam üstünden geçiyor: kanal pasif KALMALI, yeni yazım OLMAMALI.
            input.IsActive = true;
            await products.UpdateAsync(created.ProductId, input);

            (await _appService.GetAsync(created.Id)).IsActive.ShouldBeFalse(
                "Bayat graf kanal ürününü yeniden AÇMAMALI — bayrak kanal ürününün kendi formundan yönetilir.");
            _restClient.PriceStockBatches.Count.ShouldBe(batchesBefore + 1);
        }
    }

    /// <summary>③ Kanala hiç ulaşmamış (SKU'suz) kayıt: gönderilecek adet yok — N11'e istek atılmaz ama
    /// pasifleştirme YİNE de başarılıdır (atlama sessiz kalmaz, log'a düşer — iddia trafiğin yokluğunda).</summary>
    [Fact]
    public async Task A_record_that_never_reached_the_channel_is_deactivated_without_calling_n11()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "ZERO3", greenPrice: 150m, greenStock: 8);
            var batchesBefore = _restClient.PriceStockBatches.Count;

            await MutateAsync(created.Id, u => u.IsActive = false);

            (await _appService.GetAsync(created.Id)).IsActive.ShouldBeFalse();
            _restClient.PriceStockBatches.Count.ShouldBe(batchesBefore);   // pazaryerinde düşürülecek adet yok
        }
    }

    /// <summary>④ N11 adet-0'ı REDDEDERSE pasifleştirme GERİ DÖNER (aynı transaction rollback — Trendyol arşiv
    /// emsali): "bizde pasif ama N11'de stoklu satışta" hâli tam da kapatılmak istenen delik olurdu. LastSent
    /// tabanı da TERFİ ETMEZ — ulaşmamış 0, kıyas tabanını ilerletmemeli.</summary>
    [Fact]
    public async Task When_n11_rejects_the_zero_push_the_deactivation_rolls_back()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "ZERO4", greenPrice: 150m, greenStock: 8);
            await _appService.PushToN11Async(created.Id);
            _taskPoller.Result = new N11TaskResult(N11TaskState.Rejected, Array.Empty<N11TaskItemResult>(), "Ürün bulunamadı");
            try
            {
                var ex = await Should.ThrowAsync<BusinessException>(
                    () => MutateAsync(created.Id, u => u.IsActive = false));
                ex.Code.ShouldBe("TradeXpress:N11:Rest:PushRejected");

                var reloaded = await _appService.GetAsync(created.Id);
                reloaded.IsActive.ShouldBeTrue();                               // bayrak geri döndü
                reloaded.Skus.ShouldAllBe(s => s.LastSentQuantity != 0);        // taban ilerlemedi
            }
            finally
            {
                _taskPoller.Result = new N11TaskResult(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);
            }
        }
    }

    /// <summary>⑤ Geri dönüş: pasif→aktif geçişi ANINDA senkron tetikler (guard artık geçer) ve dirty-check
    /// (LastSent=0 ≠ gerçek) gerçek adetleri N11'e geri yazar — 15 dakikalık turu beklemeden. Ayrı bir
    /// "yeniden aç" mekanizması YOKTUR; bu test simetrinin kendiliğinden çalıştığını kilitler.</summary>
    [Fact]
    public async Task Reactivating_the_record_immediately_writes_the_real_quantities_back()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "ZERO5", greenPrice: 150m, greenStock: 8);
            await _appService.PushToN11Async(created.Id);
            await MutateAsync(created.Id, u => u.IsActive = false);   // adet-0 gitti, LastSent=0
            var batchesBefore = _restClient.PriceStockBatches.Count;

            await MutateAsync(created.Id, u => u.IsActive = true);

            _restClient.PriceStockBatches.Count.ShouldBe(batchesBefore + 1);
            var batch = _restClient.PriceStockBatches[^1];
            batch.Select(r => r.Quantity).OrderBy(q => q).ShouldBe(new int?[] { 8, 10, 20 });   // gerçek adetler geri
            batch.Select(r => r.SalePrice).OrderBy(p => p).ShouldBe(new decimal?[] { 100m, 100m, 150m });

            var reloaded = await _appService.GetAsync(created.Id);
            reloaded.IsActive.ShouldBeTrue();
            reloaded.Skus.Select(s => s.LastSentQuantity).OrderBy(q => q).ShouldBe(new int?[] { 8, 10, 20 });
        }
    }

    /// <summary>⑥ Adet-0 gönderimi N11 kuyruğunda kalırsa: LastSent TERFİ ETMEZ (ulaşmamış 0 taban ilerletmez),
    /// task kimliği saklanır; çözüm PASİF kayıtta LastSent'i 0'a çeker — plan dondurması OLMAZ. Plan dondurulsaydı
    /// gerçek adetler "gönderildi" yazılır, yeniden aktifleşme senkronu "değişiklik yok" der ve N11 sessizce
    /// 0'da takılı kalırdı ("gönderdim kaydı gövdeye fiilen giren setten yazılır" kuralının bu yoldaki bedeli).</summary>
    [Fact]
    public async Task A_queued_zero_push_resolves_to_zero_baseline_not_to_the_plan()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "ZERO6", greenPrice: 150m, greenStock: 8);
            await _appService.PushToN11Async(created.Id);

            // Adet-0 task'ı kuyrukta kalsın.
            _taskPoller.Result = new N11TaskResult(N11TaskState.InQueue, Array.Empty<N11TaskItemResult>(), null);
            await MutateAsync(created.Id, u => u.IsActive = false);

            var queued = await _appService.GetAsync(created.Id);
            queued.IsActive.ShouldBeFalse();
            queued.PendingPushTaskId.ShouldNotBeNullOrEmpty();
            queued.Skus.ShouldAllBe(s => s.LastSentQuantity != 0);   // sonuç yok → taban ilerlemedi

            // N11 task'ı işledi → çözüm (kuyruk işçisinin kullandığı yolla) tabanı 0'a çeker, plana DEĞİL.
            _taskPoller.Result = new N11TaskResult(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);
            var resolved = await _appService.ResolvePendingPushAsync(created.Id);

            resolved.PendingPushTaskId.ShouldBeNullOrEmpty();
            resolved.Skus.Count.ShouldBe(3);
            resolved.Skus.ShouldAllBe(s => s.LastSentQuantity == 0);
        }
    }

    /// <summary>Kaydı GERÇEK güncelleme yolundan değiştirir — entity'ye elle dokunmak UpdateAsync'in geçiş
    /// yakalamasını (adet-0 / anında senkron) baypas ederdi; kilitlenen tam da o yakalamadır.</summary>
    private async Task MutateAsync(Guid id, Action<SalesChannelTrN11ProductUpdateDto> mutate)
    {
        var dto = await _appService.GetAsync(id);
        var update = BuildUpdateDto(dto);
        mutate(update);
        await _appService.UpdateAsync(id, update);
    }

    /// <summary>⑦ PASİF kayda elle TAM PUSH reddedilir (2026-08-21 hakem bulgusu): guard olmasaydı gerçek
    /// adetli task kuyruğa düşer, çözümün pasif dalı LastSent'i 0'a çeker ve N11 gerçek stokla satıştayken
    /// sistem 0 gönderdiğine inanırdı. Satışa dönüş yolu push değil AKTİFLEŞTİRMEDİR.</summary>
    [Fact]
    public async Task A_passive_record_rejects_a_manual_full_push()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAxisProductWithN11OnlyRowAsync(companyId, "ZERO7", greenPrice: 150m, greenStock: 8);
            await _appService.PushToN11Async(created.Id);
            await MutateAsync(created.Id, u => u.IsActive = false);
            var batchesBefore = _restClient.PriceStockBatches.Count;

            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.PushToN11Async(created.Id));

            ex.Code.ShouldBe("TradeXpress:N11:Product:PassiveNoPush");
            _restClient.PriceStockBatches.Count.ShouldBe(batchesBefore, "Pasif kayda hiçbir gönderim çıkmamalı.");
        }
    }
}
