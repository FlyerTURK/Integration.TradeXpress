using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// TRENDYOL HAFİF FİYAT/STOK SENKRONU (C-1b / K6) — ürün içeriğine dokunmadan yalnız adet+fiyat yazan yol.
///
/// <para><b>Neden var:</b> çapraz-kanal aşırı satış deliği. N11'den gelen bir sipariş stoğu düşürdüğünde,
/// bu yol olmadan Trendyol bir sonraki TAM push'a kadar bayat adedi göstermeye devam eder — yani elde olmayan
/// malı satmaya devam eder.</para>
///
/// <para><b>En kritik pin (d):</b> submit'ten sonra <c>LastSent*</c> DEĞİŞMEMELİ. Trendyol yazma uçları asenkron;
/// gerçek yazım ancak batch COMPLETED olunca kesinleşir. Şimdi güncellenseydi bir sonraki tur "değişiklik yok"
/// der ve hiç yazılmamış fiyat/stok sessizce atlanırdı. Hata çıkmaz, log temiz kalır, yalnız pazaryerindeki
/// sayı yanlış olur.</para>
/// </summary>
public abstract class SalesChannelTrTrendyolProductStockSyncTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string ProductEntityName = "Product";

    private readonly ISalesChannelTrTrendyolProductAppService _appService;
    private readonly EntityVariantSynchronizer _erpSynchronizer;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _channelProductRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityAttribute, Guid> _erpAttributeRepository;
    private readonly IRepository<EntityAttributeValue, Guid> _erpValueRepository;
    private readonly IRepository<EntityVariant, Guid> _erpVariantRepository;
    private readonly IRepository<ProductVariantDetail, Guid> _variantDetailRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly IRepository<SalesChannelTrTrendyolProductPushHistory, Guid> _historyRepository;
    private readonly FakeTrendyolProductClient _client;

    protected SalesChannelTrTrendyolProductStockSyncTests()
    {
        _appService = GetRequiredService<ISalesChannelTrTrendyolProductAppService>();
        _erpSynchronizer = GetRequiredService<EntityVariantSynchronizer>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrTrendyol, Guid>>();
        _channelProductRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProduct, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _erpAttributeRepository = GetRequiredService<IRepository<EntityAttribute, Guid>>();
        _erpValueRepository = GetRequiredService<IRepository<EntityAttributeValue, Guid>>();
        _erpVariantRepository = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _variantDetailRepository = GetRequiredService<IRepository<ProductVariantDetail, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
        _historyRepository = GetRequiredService<IRepository<SalesChannelTrTrendyolProductPushHistory, Guid>>();
        _client = GetRequiredService<FakeTrendyolProductClient>();
        _client.AllowPriceInventoryWrites = true;
    }

    /// <summary>(a) Hiç SKU donmamışsa senkron YAPILMAZ — barkodsuz gönderim Trendyol'da adressiz yazma olurdu.</summary>
    [Fact]
    public async Task Sync_without_any_frozen_sku_is_rejected()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYSYNC1", verify: true, seedSkus: false);

            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.SyncStockAndPriceAsync(created.Id));

            ex.Code.ShouldBe("TradeXpress:Trendyol:Product:NotPushedYet");
        }
    }

    /// <summary>(c) Tek SKU bile dirty ise BİLİNEN TÜM SKU'lar gönderilir. Yalnız değişeni göndermek, Trendyol'un
    /// kısmi gövdeyi nasıl birleştirdiğine bel bağlamak olurdu — N11'de aynı gerekçeyle böyle.</summary>
    [Fact]
    public async Task A_single_dirty_sku_sends_every_known_sku()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYSYNC2", verify: true, seedSkus: true);

            await _appService.SyncStockAndPriceAsync(created.Id);

            _client.PriceInventoryBatches.Count.ShouldBe(1);
            _client.PriceInventoryBatches[0].Count.ShouldBe(2);   // RED + BLUE
            _client.PriceInventoryBatches[0].Select(i => i.Quantity).ShouldBe(new int?[] { 10, 20 }, ignoreOrder: true);
        }
    }

    /// <summary>(d) KUYRUK TUZAĞI — submit'ten sonra <c>LastSent*</c> DEĞİŞMEZ (batch hâlâ PROCESSING).
    /// Bu testin sabotajı en sinsi regresyonu açar: "gönderdim sayıldı ama gitmedi".</summary>
    [Fact]
    public async Task Submitting_does_not_advance_the_last_sent_values()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYSYNC3", verify: true, seedSkus: true);

            var synced = await _appService.SyncStockAndPriceAsync(created.Id);

            synced.BatchRequestId.ShouldNotBeNullOrEmpty();
            synced.Skus.ShouldAllBe(s => s.LastSentQuantity == null);
            synced.Skus.ShouldAllBe(s => s.LastSentListPrice == null);
        }
    }

    /// <summary>(f) Önceki fiyat/stok batch'i işlenirken İKİNCİ submit yapılmaz — Trendyol aynı gövdeyi 15 dk
    /// içinde mükerrer sayıp reddediyor, üstelik iki açık batch'in hangisinin kazandığı belirsiz.</summary>
    [Fact]
    public async Task A_second_submit_is_refused_while_the_previous_batch_is_processing()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYSYNC4", verify: true, seedSkus: true);
            await _appService.SyncStockAndPriceAsync(created.Id);
            var batchesAfterFirst = _client.PriceInventoryBatches.Count;

            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.SyncStockAndPriceAsync(created.Id));

            ex.Code.ShouldBe("TradeXpress:Trendyol:Product:BatchInProgress");
            _client.PriceInventoryBatches.Count.ShouldBe(batchesAfterFirst);
        }
    }

    /// <summary>(e) HK-3 GEÇİŞ KİPİ — hiç doğrulanmış varyantı olmayan ürün senkron kapsamı DIŞINDA kalır:
    /// Trendyol'a İSTEK GİTMEZ ve kullanıcı "doğrulama bekliyor" uyarısını görür.
    ///
    /// <para>Alternatif (a) kipinde bu ürünün tüm SKU'larına adet 0 giderdi ve canlıdaki 103 listeleme
    /// sınıflandırma bitene kadar topluca kapanırdı. Karar (b): sessiz değil ama kapatmayan yol.</para></summary>
    [Fact]
    public async Task A_product_with_no_verified_variant_stays_out_of_sync_scope()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYSYNC5", verify: false, seedSkus: true);

            var synced = await _appService.SyncStockAndPriceAsync(created.Id);

            _client.PriceInventoryBatches.ShouldBeEmpty();          // Trendyol'a HİÇ istek gitmedi
            synced.SyncWarnings.ShouldNotBeEmpty();                 // ama SESSİZ de değil
            synced.BatchRequestId.ShouldBeNullOrEmpty();
        }
    }

    /// <summary>Doğrulanmamış varyant push ADAYI olmaz — kapı fiyatlamadan öncedir (§6 statü güvenliği).
    /// Trendyol'da bu kapı bugüne kadar HİÇ yoktu; N11'de vardı.</summary>
    [Fact]
    public async Task Unverified_variants_never_reach_the_preview_rows()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var unverified = await SeedAsync(companyId, "TYGATE1", verify: false, seedSkus: false);
            var verified = await SeedAsync(companyId, "TYGATE2", verify: true, seedSkus: false);

            (await _appService.GetPushPreviewAsync(unverified.Id)).Items.ShouldBeEmpty();
            (await _appService.GetPushPreviewAsync(verified.Id)).Items.Count.ShouldBe(2);
        }
    }

    /// <summary>Emniyet payı Trendyol satırlarına da uygulanır — P2'nin Trendyol ayağı burada yeşillenir
    /// (alan o dilimde açılmış, tüketimi bu dilimde bağlanmıştı).</summary>
    [Fact]
    public async Task Safety_stock_is_applied_to_trendyol_rows()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYSAFE", verify: true, seedSkus: true, safetyStock: 4);

            await _appService.SyncStockAndPriceAsync(created.Id);

            _client.PriceInventoryBatches[0].Select(i => i.Quantity)
                .ShouldBe(new int?[] { 6, 16 }, ignoreOrder: true);   // 10−4, 20−4
        }
    }

    /// <summary>KISMİ ELEME — ürün kapsamda ama BİR varyantı satışa uygun değil.
    ///
    /// <para><b>Bu testin yakaladığı açık (2026-08-08 adversaryel incelemesinde bulundu):</b> eleme sessizce
    /// atlanıyordu. Varyant kapıya takıldığı için aday olmuyor, ama Trendyol'da SON GÖNDERİLEN adetle CANLI
    /// duruyor ve sipariş almaya devam ediyordu — üstelik bir daha ASLA tazelenmiyordu. Sistem "bu varyant
    /// satılmamalı" kararını kendi veriyor, pazaryerine hiç bildirmiyordu. Tam da bu dilimin kapatmaya
    /// çalıştığı aşırı satış penceresi, kapının kendi içinde yeniden açılmıştı.</para>
    ///
    /// <para>Doğru davranış: o SKU'ya <b>adet 0</b> gider (fiyata dokunulmaz) — §6 ① kararının SKU granülünde
    /// uygulanışı. Kalan varyantları engellemek meşru işi durdururdu.</para></summary>
    [Fact]
    public async Task A_variant_that_falls_out_of_the_gate_is_zeroed_not_silently_skipped()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYPART", verify: true, seedSkus: true);

            // BLUE varyantını ASKIYA AL — bu, kısmi elemenin GERÇEK üreticisidir: emtia pasifleştirilince
            // ProductSaleSuspender tasarım gereği yalnız etkilenen varyantı askıya alır, kardeşleri satışta kalır.
            await WithUnitOfWorkAsync(async () =>
            {
                var product = await _productRepository.FirstAsync(p => p.Code == "TYPART");
                var blue = (await _erpVariantRepository.GetListAsync(
                    v => v.EntityName == ProductEntityName && v.EntityId == product.Id)).Single(v => v.Code == "BLUE");
                var detail = await _variantDetailRepository.FirstAsync(d => d.EntityVariantId == blue.Id);
                detail.Suspend();
                await _variantDetailRepository.UpdateAsync(detail, autoSave: true);
            });

            var synced = await _appService.SyncStockAndPriceAsync(created.Id);

            // İKİ SKU da gönderildi — biri gerçek adediyle, biri KAPATILARAK.
            _client.PriceInventoryBatches.Count.ShouldBe(1);
            var gonderilen = _client.PriceInventoryBatches[0];
            gonderilen.Count.ShouldBe(2);

            var kapatilan = gonderilen.Single(i => i.Barcode.EndsWith("BLUE", StringComparison.Ordinal));
            kapatilan.Quantity.ShouldBe(0);
            kapatilan.ListPrice.ShouldBeNull();   // fiyata DOKUNULMAZ
            kapatilan.SalePrice.ShouldBeNull();

            gonderilen.Single(i => i.Barcode.EndsWith("RED", StringComparison.Ordinal)).Quantity.ShouldBe(10);

            // Ve SESSİZ değil.
            synced.SyncWarnings.ShouldNotBeEmpty();
        }
    }

    /// <summary>FİYAT BANDI TRENDYOL AYAĞI — bu dilime kadar bandın Trendyol tarafında HİÇ testi yoktu
    /// (yalnız N11'de vardı); dört ayrı sabotaj tüm testler yeşilken geçebiliyordu.</summary>
    [Fact]
    public async Task A_price_outside_the_band_stops_the_trendyol_sync()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYBAND", verify: true, seedSkus: true, minPrice: 500m);

            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.SyncStockAndPriceAsync(created.Id));

            ex.Code.ShouldBe("TradeXpress:SalesChannel:Product:PriceOutOfBand");
            _client.PriceInventoryBatches.ShouldBeEmpty();

            // TEŞHİS KAYBOLMAZ: LastError ham kod değil, guard'ın doldurduğu veriyle birlikte okunur metin taşır.
            var reloaded = await _appService.GetAsync(created.Id);
            reloaded.LastError.ShouldNotBeNullOrEmpty();
            reloaded.LastError!.ShouldNotContain("TradeXpress:");   // anahtar değil, cümle
        }
    }

    /// <summary>Bant İÇİNDEKİ fiyat geçer — guard'ın meşru işi engellemediği de pinli.</summary>
    [Fact]
    public async Task A_price_inside_the_band_passes_on_trendyol()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYBAND2", verify: true, seedSkus: true, minPrice: 10m);

            await _appService.SyncStockAndPriceAsync(created.Id);

            _client.PriceInventoryBatches.Count.ShouldBe(1);
        }
    }

    /// <summary>DEVAM EDEN CREATE BATCH'İ KORUNUR — guard tipe bakmaz. Bakmasaydı senkron, create'in
    /// makbuzunu (tek <c>BatchRequestId</c> yuvası) ezer ve o push'un akıbeti bir daha sorgulanamazdı.</summary>
    [Fact]
    public async Task Sync_refuses_to_overwrite_an_in_flight_create_batch()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYBATCH", verify: true, seedSkus: true);

            await WithUnitOfWorkAsync(async () =>
            {
                var entity = await _channelProductRepository.GetAsync(created.Id);
                entity.MarkSubmitted("CREATE-BATCH-1", "ProductV2OnBoarding", DateTime.UtcNow);
                await _channelProductRepository.UpdateAsync(entity, autoSave: true);
            });

            var ex = await Should.ThrowAsync<BusinessException>(() => _appService.SyncStockAndPriceAsync(created.Id));

            ex.Code.ShouldBe("TradeXpress:Trendyol:Product:BatchInProgress");
            (await _appService.GetAsync(created.Id)).BatchRequestId.ShouldBe("CREATE-BATCH-1");
        }
    }

    /// <summary>COMPLETED batch → gönderilen değerler <c>LastSent*</c>'e TERFİ eder ve ikinci senkron
    /// "değişiklik yok" der. Dirty-check'in kıyas tabanı ancak burada dolar — P5'in bütün gerekçesi bu.</summary>
    [Fact]
    public async Task A_completed_batch_promotes_the_sent_values_and_the_next_sync_is_a_no_op()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYFIN1", verify: true, seedSkus: true);
            await _appService.SyncStockAndPriceAsync(created.Id);

            _client.NextBatchStatus = new TrendyolBatchStatus("COMPLETED", 2, 0, null);
            var refreshed = await _appService.RefreshStatusAsync(created.Id);

            refreshed.Skus.Select(s => s.LastSentQuantity).ShouldBe(new int?[] { 10, 20 }, ignoreOrder: true);

            var batchesBefore = _client.PriceInventoryBatches.Count;
            var second = await _appService.SyncStockAndPriceAsync(created.Id);

            _client.PriceInventoryBatches.Count.ShouldBe(batchesBefore);   // Trendyol'a istek GİTMEDİ
            second.SyncWarnings.ShouldNotBeEmpty();
        }
    }

    /// <summary>COMPLETED batch SKU başına GEÇMİŞ satırı üretir — delil zinciri buradan başlar.</summary>
    [Fact]
    public async Task A_completed_batch_writes_push_history()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYFIN2", verify: true, seedSkus: true);
            await _appService.SyncStockAndPriceAsync(created.Id);

            _client.NextBatchStatus = new TrendyolBatchStatus("COMPLETED", 2, 0, null);
            await _appService.RefreshStatusAsync(created.Id);

            var history = await WithUnitOfWorkAsync(() => _historyRepository.GetListAsync(
                h => h.SalesChannelTrTrendyolProductId == created.Id));

            history.Count.ShouldBe(2);
            history.ShouldAllBe(h => h.PushKind == TrendyolProductPushKind.PriceStockSync);
            history.Select(h => h.Quantity).ShouldBe(new int?[] { 10, 20 }, ignoreOrder: true);
            history.ShouldAllBe(h => h.BatchRequestId != null);
        }
    }

    /// <summary>FAILED batch → <c>LastSent*</c> DEĞİŞMEZ, geçmişe satır YAZILMAZ, bekleyenler atılır.
    /// Reddedilen gönderimi delil defterinde başarılı göstermek defteri delil olmaktan çıkarırdı; tabanı
    /// terfi ettirmek ise gönderilmemiş değerleri "senkron" sayardı.</summary>
    [Fact]
    public async Task A_failed_batch_neither_promotes_nor_records()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var created = await SeedAsync(companyId, "TYFIN3", verify: true, seedSkus: true);
            await _appService.SyncStockAndPriceAsync(created.Id);

            _client.NextBatchStatus = new TrendyolBatchStatus("FAILED", 2, 2, "barcode not found");
            var refreshed = await _appService.RefreshStatusAsync(created.Id);

            refreshed.Skus.ShouldAllBe(s => s.LastSentQuantity == null);

            var history = await WithUnitOfWorkAsync(() => _historyRepository.GetListAsync(
                h => h.SalesChannelTrTrendyolProductId == created.Id));
            history.ShouldBeEmpty();
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Kanal + ürün + iki ERP varyantı (RED 10 adet, BLUE 20 adet, ikisi de 100 TL) kurar.
    /// <paramref name="verify"/> false ise varyantlar İNSAN onayından geçmemiş sayılır (kapı testleri).
    /// <paramref name="seedSkus"/> true ise kayıt "daha önce push edilmiş" gibi barkodlu SKU satırları alır.</summary>
    private async Task<SalesChannelTrTrendyolProductDto> SeedAsync(
        Guid companyId, string productCode, bool verify, bool seedSkus, int? safetyStock = null, decimal? minPrice = null)
    {
        var (channel, product) = await WithUnitOfWorkAsync(async () =>
        {
            var ch = await _channelRepository.InsertAsync(
                new SalesChannelTrTrendyol(companyId, $"TY-{productCode}", $"Trendyol {productCode}", "seller-1", "api-key", "api-secret"),
                autoSave: true);
            var pr = await _productRepository.InsertAsync(new Product(companyId, productCode, $"Urun {productCode}"), autoSave: true);
            return (ch, pr);
        });

        await SeedErpVariantsAsync(companyId, product, verify, ("Red", 100m, 10), ("Blue", 100m, 20));

        var created = await _appService.CreateAsync(new SalesChannelTrTrendyolProductCreateDto
        {
            ProductId = product.Id,
            SalesChannelId = channel.Id,
            CategoryId = "411",
            BrandId = "1",
            VatRate = 20,
            SafetyStock = safetyStock,
            MinPrice = minPrice,
        });

        if (seedSkus)
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var entity = await _channelProductRepository.GetAsync(created.Id);
                var variants = await _erpVariantRepository.GetListAsync(
                    v => v.EntityName == ProductEntityName && v.EntityId == product.Id);
                foreach (var v in variants)
                {
                    entity.UpsertImportedSku(v.Id, $"BC-{productCode}-{v.Code}", v.Code, remoteContentId: 1);
                }

                await _channelProductRepository.UpdateAsync(entity, autoSave: true);
            });
        }

        return created;
    }

    private async Task SeedErpVariantsAsync(
        Guid companyId, Product product, bool verify, params (string Value, decimal Price, int Stock)[] values)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var attribute = await _erpAttributeRepository.InsertAsync(
                new EntityAttribute(companyId, ProductEntityName, product.Id, "Renk", 0), autoSave: true);
            for (var i = 0; i < values.Length; i++)
            {
                await _erpValueRepository.InsertAsync(
                    new EntityAttributeValue(companyId, attribute.Id, values[i].Value, i), autoSave: true);
            }

            await _erpSynchronizer.SynchronizeAsync(ProductEntityName, product.Id, companyId, product.Name);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var variants = await _erpVariantRepository.GetListAsync(
                v => v.EntityName == ProductEntityName && v.EntityId == product.Id);
            foreach (var (value, price, stock) in values)
            {
                var variant = variants.Single(v => v.Code == value.ToUpperInvariant());
                variant.SetStock(stock);
                await _erpVariantRepository.UpdateAsync(variant, autoSave: true);

                var detail = new ProductVariantDetail(companyId, variant.Id);
                detail.SetSalePrice(price, null);

                // PUSH KAPISI (§6): varyant aday listesine ancak İNSAN onayıyla girer. verify=false olan
                // fixture tam da bu kapıyı sınamak içindir — damga BASILMAZ.
                if (verify)
                {
                    detail.MarkVerified(RecipeVerificationStamp.EmptyRecipe, DateTime.UtcNow, verifiedBy: null);
                }

                await _variantDetailRepository.InsertAsync(detail, autoSave: true);
            }
        });
    }
}
