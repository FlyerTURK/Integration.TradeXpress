using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Mocks.N11;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 sahte deposunun DAVRANIŞ sözleşmesi.
///
/// <para>Buradaki en önemli test <see cref="Product_is_not_visible_until_its_task_matures"/>: mutasyonun
/// task olgunlaşmadan uygulanmaması, sahte sunucunun sadakatinin bel kemiğidir. Sıra ters kurulursa uygulama
/// push'tan hemen sonra ürünü bulur, hiç işlenmemiş bir task'ı başarılı sayar ve <c>MarkSynced</c> ile yarım
/// gerçek yazar — hata da ancak canlıda görülür.</para>
/// </summary>
public class N11MockStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"n11-mock-{Guid.NewGuid():N}.json");

    private N11MockStore NewStore(int queuedPolls = 0)
    {
        return new N11MockStore(_path, queuedPolls);
    }

    private static List<N11MockProduct> OneProduct(string stockCode = "TEST-1", decimal price = 100m)
    {
        return new List<N11MockProduct>
        {
            new()
            {
                StockCode = stockCode,
                ProductMainId = "GRUP-1",
                Title = "Test Ürünü",
                SalePrice = price,
                ListPrice = price,
                Quantity = 3,
                CategoryId = "1001",
            },
        };
    }

    [Fact]
    public async Task Submit_returns_a_task_id_and_leaves_it_queued()
    {
        var store = NewStore(queuedPolls: 1);

        var taskId = await store.SubmitAsync("PRODUCT_CREATE", OneProduct());

        taskId.ShouldNotBeNullOrWhiteSpace();
        var task = await store.PollTaskAsync(taskId);
        task!.Status.ShouldBe(N11MockTaskStates.InQueue);   // eşik 1, ilk sorgu henüz olgunlaştırmaz
    }

    /// <summary>SAHTE SUNUCUNUN EN KRİTİK KURALI — ürün, task'ı olgunlaşana dek katalogda GÖRÜNMEZ.</summary>
    [Fact]
    public async Task Product_is_not_visible_until_its_task_matures()
    {
        var store = NewStore(queuedPolls: 1);
        var taskId = await store.SubmitAsync("PRODUCT_CREATE", OneProduct());

        // Gönderim yapıldı ama hiç sorgulanmadı → mağaza BOŞ.
        var before = await store.QueryProductsAsync(0, 50, null, null);
        before.Items.ShouldBeEmpty();

        await store.PollTaskAsync(taskId);   // 1. sorgu: hâlâ kuyrukta
        (await store.QueryProductsAsync(0, 50, null, null)).Items.ShouldBeEmpty();

        var matured = await store.PollTaskAsync(taskId);   // 2. sorgu: eşik aşıldı → olgunlaştı
        matured!.Status.ShouldBe(N11MockTaskStates.Processed);

        var after = await store.QueryProductsAsync(0, 50, null, null);
        after.Items.ShouldHaveSingleItem().StockCode.ShouldBe("TEST-1");
    }

    [Fact]
    public async Task Matured_product_gets_a_ten_digit_identity()
    {
        var store = NewStore();
        var taskId = await store.SubmitAsync("PRODUCT_CREATE", OneProduct());
        await store.PollTaskAsync(taskId);

        var product = (await store.QueryProductsAsync(0, 50, null, null)).Items.ShouldHaveSingleItem();
        product.N11ProductId.ShouldBeGreaterThan(1000000000L);   // doküman: 9→10 haneye çıkabilir
        product.ProductStatus.ShouldBe("Active");
        product.SaleStatus.ShouldBe("On_Sale");
    }

    /// <summary>Aynı stok kodu ikinci kez push edilirse ürün ÇOĞALMAZ ve kimliği KORUNUR — idempotency.</summary>
    [Fact]
    public async Task Second_push_of_the_same_stock_code_updates_instead_of_duplicating()
    {
        var store = NewStore();
        var first = await store.SubmitAsync("PRODUCT_CREATE", OneProduct(price: 100m));
        await store.PollTaskAsync(first);
        var originalId = (await store.QueryProductsAsync(0, 50, null, null)).Items.Single().N11ProductId;

        var second = await store.SubmitAsync("PRICE_STOCK_UPDATE", OneProduct(price: 250m));
        await store.PollTaskAsync(second);

        var products = await store.QueryProductsAsync(0, 50, null, null);
        var product = products.Items.ShouldHaveSingleItem();
        product.N11ProductId.ShouldBe(originalId);   // kimlik BİR KEZ atanır
        product.SalePrice.ShouldBe(250m);
    }

    /// <summary>Fahiş fiyat senaryosu RESMÎ metni döndürmeli — uygulamanın özel hata kodu bu metne bakıyor.</summary>
    [Fact]
    public async Task Price_band_scenario_fails_the_item_with_the_official_wording()
    {
        WriteScenario(new { scenario = new { mode = N11MockModes.PriceBand, queuedPollsBeforeProcessed = 0 } });
        var store = NewStore();

        var taskId = await store.SubmitAsync("PRODUCT_CREATE", OneProduct());
        var task = await store.PollTaskAsync(taskId);

        task!.Status.ShouldBe(N11MockTaskStates.Reject);   // hiçbir kalem geçmedi → task REJECT
        var item = task.Results.ShouldHaveSingleItem();
        item.Status.ShouldBe(N11MockTaskStates.ItemFailed);
        item.Reason.ShouldContain("fahiş fiyat");   // uygulamanın PriceOutOfBand eşlemesinin tetikleyicisi

        // Reddedilen kalem mağazaya İŞLENMEZ.
        (await store.QueryProductsAsync(0, 50, null, null)).Items.ShouldBeEmpty();
    }

    /// <summary>"Queued" kipi task'ı hiç olgunlaştırmaz — uygulamanın bekleyen-push yolunu sınamak için.</summary>
    [Fact]
    public async Task Queued_scenario_never_matures()
    {
        WriteScenario(new { scenario = new { mode = N11MockModes.Queued } });
        var store = NewStore();

        var taskId = await store.SubmitAsync("PRODUCT_CREATE", OneProduct());
        for (var i = 0; i < 5; i++)
        {
            (await store.PollTaskAsync(taskId))!.Status.ShouldBe(N11MockTaskStates.InQueue);
        }

        (await store.QueryProductsAsync(0, 50, null, null)).Items.ShouldBeEmpty();
    }

    /// <summary>Stok kodu bazında override: bir SKU düşer, diğeri geçer — kısmi başarı gerçek N11 davranışıdır.</summary>
    [Fact]
    public async Task Per_stock_code_override_produces_partial_success()
    {
        WriteScenario(new
        {
            scenario = new
            {
                mode = N11MockModes.Success,
                perStockCode = new Dictionary<string, string> { ["BAD-1"] = N11MockModes.PriceBand },
            },
        });
        var store = NewStore();

        var items = OneProduct("GOOD-1");
        items.AddRange(OneProduct("BAD-1"));
        var taskId = await store.SubmitAsync("PRODUCT_CREATE", items);
        var task = await store.PollTaskAsync(taskId);

        // En az bir kalem geçtiği için task PROCESSED — ama kalem sonuçları AYRIŞIR.
        task!.Status.ShouldBe(N11MockTaskStates.Processed);
        task.Results.Single(r => r.StockCode == "GOOD-1").Status.ShouldBe(N11MockTaskStates.ItemSuccess);
        task.Results.Single(r => r.StockCode == "BAD-1").Status.ShouldBe(N11MockTaskStates.ItemFailed);

        (await store.QueryProductsAsync(0, 50, null, null)).Items.ShouldHaveSingleItem().StockCode.ShouldBe("GOOD-1");
    }

    [Fact]
    public async Task Unknown_task_id_returns_null()
    {
        (await NewStore().PollTaskAsync("yok-boyle-bir-task")).ShouldBeNull();
    }

    [Fact]
    public async Task Stock_code_filter_narrows_the_catalog()
    {
        var store = NewStore();
        var items = OneProduct("A-1");
        items.AddRange(OneProduct("B-1"));
        await store.PollTaskAsync(await store.SubmitAsync("PRODUCT_CREATE", items));

        var filtered = await store.QueryProductsAsync(0, 50, "B-1", null);

        filtered.Items.ShouldHaveSingleItem().StockCode.ShouldBe("B-1");
        filtered.TotalCount.ShouldBe(1);
    }

    /// <summary>Bozuk JSON host'u DÜŞÜRMEMELİ — dosya elle düzenleniyor, bir virgül hatası her şeyi durdurmamalı.</summary>
    [Fact]
    public async Task Corrupt_store_file_falls_back_to_a_fresh_state()
    {
        File.WriteAllText(_path, "{ bu gecerli JSON degil ");

        var taskId = await NewStore().SubmitAsync("PRODUCT_CREATE", OneProduct());

        taskId.ShouldNotBeNullOrWhiteSpace();
    }

    private void WriteScenario(object payload)
    {
        File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(payload));
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
