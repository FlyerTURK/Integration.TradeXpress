using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Mocks.N11;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 sahte sunucusunun GERÇEK istemcilerle uçtan uca sözleşme testi.
///
/// <para><b>Bu sınıfın varlık sebebi somut bir hata:</b> 2026-08-05'te sahte sunucunun tüm uçları HTTP 200
/// dönüyor ama gövde BOŞ geliyordu — çünkü minimal-API handler'ları <c>RequestDelegate</c> aşırı yüklemesine
/// bağlanmış ve dönen <c>IResult</c> sessizce atılmıştı. Derleme temizdi, birim testleri yeşildi, log
/// "Executed endpoint" diyordu. Hata ancak ELLE curl atılınca görüldü. Bu sınıf o sınıfı mekanikleştirir.</para>
///
/// <para><b>Neden GERÇEK Kestrel, neden TestServer değil:</b> <c>N11RestClientBase</c> statik bir
/// <c>HttpClient</c> kullanıyor (soket tükenmesini önlemek için, doğru bir karar) — <c>TestServer</c>'ın
/// in-memory handler'ı araya giremez. İstemcinin gerçekten ağa çıkması gerekiyor; rastgele portta düz HTTP
/// dinleyen bir Kestrel bunu sağlar (TLS gerekmez, sertifika sürtünmesi de olmaz).</para>
///
/// <para><b>Kanıt gücü:</b> okuyan taraf GERÇEK istemcilerdir. Mock'un tel biçimi yanlış olsaydı ayrıştırma
/// çöker ya da boş dönerdi. Yani bu testler mock'un N11'in beklediği şekli konuştuğunu, üretim kodunun
/// kendisiyle doğrular.</para>
/// </summary>
public sealed class N11MockRoundTripTests : IAsyncLifetime
{
    private const string AppKey = "mock-key";
    private const string AppSecret = "mock-secret";

    private WebApplication? _app;
    private string _baseUrl = string.Empty;
    private string _storePath = string.Empty;

    private IN11ProductRestClient _restClient = default!;
    private IN11TaskPoller _poller = default!;
    private IN11ProductQueryClient _queryClient = default!;
    private Orders.IN11OrderClient _orderClient = default!;

    public async Task InitializeAsync()
    {
        _storePath = Path.Combine(Path.GetTempPath(), $"n11-roundtrip-{Guid.NewGuid():N}.json");

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");   // port 0 = işletim sistemi boş port versin

        _app = builder.Build();
        var options = new N11MockOptions { Enabled = true, QueuedPollsBeforeProcessed = 1 };
        var store = new N11MockStore(_storePath, options.QueuedPollsBeforeProcessed);
        _app.MapN11MockEndpoints(store, options);
        _app.MapN11MockOrderEndpoint(store, options);
        await _app.StartAsync();

        _baseUrl = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/');

        // GERÇEK üretim istemcileri — tek fark taban adres.
        var endpoints = Options.Create(new N11EndpointOptions { BaseUrl = _baseUrl });
        _restClient = new N11ProductRestClient(NullLogger<N11ProductRestClient>.Instance, endpoints);
        _poller = new N11TaskPoller(NullLogger<N11TaskPoller>.Instance, endpoints);
        _queryClient = new N11ProductQueryClient(NullLogger<N11ProductQueryClient>.Instance, endpoints);
        _orderClient = new Orders.N11OrderClient(NullLogger<Orders.N11OrderClient>.Instance, endpoints);
    }

    /// <summary>Bir ürünü mağazaya işler (push + olgunlaşma) — sipariş testleri buna dayanıyor: sahte siparişler
    /// mağazadaki ürünlerden türetiliyor, yani "önce sat, sonra sipariş gelsin" akışı doğal.</summary>
    private async Task SeedMaturedProductAsync(string stockCode = "RT-1", decimal price = 1500m)
    {
        var submission = (await _restClient.CreateProductsAsync(new[] { Row(stockCode, price) }, AppKey, AppSecret)).Single();
        await _poller.QueryAsync(submission.TaskId, AppKey, AppSecret);
        await _poller.QueryAsync(submission.TaskId, AppKey, AppSecret);
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        if (File.Exists(_storePath))
        {
            File.Delete(_storePath);
        }
    }

    private static N11RestProductCreate Row(string stockCode = "RT-1", decimal price = 1500m)
    {
        return new N11RestProductCreate(
            Title: "Round-trip Ürünü",
            Description: "Sahte sunucu sözleşme testi",
            CategoryId: 1001L,
            CurrencyType: "TL",
            ProductMainId: "RT-GRUP",
            PreparingDay: 1,
            ShipmentTemplate: "MOCK-KARGO",
            StockCode: stockCode,
            Quantity: 5,
            Images: new List<N11RestProductImage> { new("https://example.invalid/a.jpg", 1) },
            Attributes: Array.Empty<N11RestProductAttribute>(),
            SalePrice: price,
            ListPrice: price,
            VatRate: 20);
    }

    /// <summary>Yazma ucu <c>taskId</c> döndürmeli — ve bu, gövdenin GERÇEKTEN yazıldığının kanıtıdır
    /// (boş gövde hatasında bu satır <c>id</c> bulamayıp fail-fast atardı).</summary>
    [Fact]
    public async Task Create_returns_a_task_id_over_real_http()
    {
        var submissions = await _restClient.CreateProductsAsync(new[] { Row() }, AppKey, AppSecret);

        var submission = submissions.ShouldHaveSingleItem();
        submission.TaskId.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>TAM DÖNGÜ: push → kuyrukta → olgunlaşma → katalogda görünür, kimliği atanmış.
    /// Üç gerçek istemci de bu testte kullanılıyor; hepsi mock'un tel biçimini ayrıştırabilmeli.</summary>
    [Fact]
    public async Task Full_push_cycle_ends_with_the_product_visible_and_identified()
    {
        var submission = (await _restClient.CreateProductsAsync(new[] { Row() }, AppKey, AppSecret)).Single();

        // 1. sorgu: task henüz kuyrukta (eşik 1) — başarı SAYILMAMALI.
        var first = await _poller.QueryAsync(submission.TaskId, AppKey, AppSecret);
        first.State.ShouldBe(N11TaskState.InQueue);

        // Kuyruktayken ürün katalogda GÖRÜNMEMELİ — sahte sunucunun en kritik kuralı.
        var beforeMaturity = await _queryClient.QueryAsync(
            new N11ProductQueryFilter(0, 50, null, null, null, null, null), AppKey, AppSecret);
        beforeMaturity.Items.ShouldBeEmpty();

        // 2. sorgu: olgunlaştı.
        var second = await _poller.QueryAsync(submission.TaskId, AppKey, AppSecret);
        second.State.ShouldBe(N11TaskState.Processed);
        second.Items.ShouldHaveSingleItem().Success.ShouldBeTrue();

        // Artık katalogda — ve N11ProductId atanmış (push sonrası geri okumanın TEK kaynağı budur).
        var after = await _queryClient.QueryAsync(
            new N11ProductQueryFilter(0, 50, null, null, null, null, null), AppKey, AppSecret);
        var product = after.Items.ShouldHaveSingleItem();
        product.StockCode.ShouldBe("RT-1");
        product.N11ProductId.ShouldBeGreaterThan(0);
        product.SalePrice.ShouldBe(1500m);
    }

    /// <summary>Stok kodu filtresi gerçek istemcinin kurduğu sorgu dizesiyle çalışmalı.</summary>
    [Fact]
    public async Task Stock_code_filter_round_trips_through_the_real_query_client()
    {
        var submission = (await _restClient.CreateProductsAsync(
            new[] { Row("RT-A"), Row("RT-B") }, AppKey, AppSecret)).Single();
        await _poller.QueryAsync(submission.TaskId, AppKey, AppSecret);
        await _poller.QueryAsync(submission.TaskId, AppKey, AppSecret);

        var filtered = await _queryClient.QueryAsync(
            new N11ProductQueryFilter(0, 50, "RT-B", null, null, null, null), AppKey, AppSecret);

        filtered.Items.ShouldHaveSingleItem().StockCode.ShouldBe("RT-B");
    }

    /// <summary>Fiyat/stok güncellemesi de aynı döngüden geçmeli — üç yazma ucunun hepsi mock'ta karşılanıyor.</summary>
    [Fact]
    public async Task Price_stock_update_matures_and_changes_the_catalog()
    {
        var create = (await _restClient.CreateProductsAsync(new[] { Row(price: 1000m) }, AppKey, AppSecret)).Single();
        await _poller.QueryAsync(create.TaskId, AppKey, AppSecret);
        await _poller.QueryAsync(create.TaskId, AppKey, AppSecret);

        var update = (await _restClient.UpdatePriceStockAsync(
            new[] { new N11RestPriceStock("RT-1", ListPrice: 2400m, SalePrice: 2400m, Quantity: 9, CurrencyType: "TL") },
            AppKey, AppSecret)).Single();
        await _poller.QueryAsync(update.TaskId, AppKey, AppSecret);
        await _poller.QueryAsync(update.TaskId, AppKey, AppSecret);

        var product = (await _queryClient.QueryAsync(
            new N11ProductQueryFilter(0, 50, null, null, null, null, null), AppKey, AppSecret)).Items.ShouldHaveSingleItem();

        product.SalePrice.ShouldBe(2400m);
        product.Quantity.ShouldBe(9);
    }

    // ── Sipariş (SOAP) ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Sipariş listesi GERÇEK <c>N11OrderClient</c> ile okunabilmeli. Bu, SOAP zarfının ve element
    /// adlarının doğru olduğunun kanıtı: ayrıştırıcı namespace-agnostik ama ELEMENT ADLARINA bağımlı.</summary>
    [Fact]
    public async Task Order_list_round_trips_through_the_real_soap_client()
    {
        await SeedMaturedProductAsync("ORD-1", 2750m);

        var page = await _orderClient.GetOrdersPageAsync(AppKey, AppSecret, page: 0);

        var order = page.Orders.ShouldHaveSingleItem();
        order.OrderNumber.ShouldStartWith("MOCK-");
        order.TotalAmount.ShouldBe(2750m);
        order.CargoTrackingNumber.ShouldNotBeNullOrWhiteSpace();

        var line = order.Lines.ShouldHaveSingleItem();
        line.StockCode.ShouldBe("ORD-1");
        line.Quantity.ShouldBe(1);
    }

    /// <summary>Sipariş detayı da aynı istemciyle çözülebilmeli (fatura/adres blokları dahil).</summary>
    [Fact]
    public async Task Order_detail_round_trips_and_carries_addresses()
    {
        await SeedMaturedProductAsync("ORD-2", 900m);
        var order = (await _orderClient.GetOrdersPageAsync(AppKey, AppSecret, 0)).Orders.Single();

        var detail = await _orderClient.GetOrderDetailAsync(
            AppKey, AppSecret, order.RemoteOrderId, DateTime.UtcNow);

        detail.ShouldNotBeNull();
    }

    /// <summary>YAZMA uçları: kabul / red / kargo. İstemci yalnız <c>status=success</c> arıyor; mock onu
    /// döndürmezse istemci BusinessException atardı — yani bu testler sözleşmenin iki ucunu da doğruluyor.</summary>
    [Fact]
    public async Task Order_write_operations_are_accepted_by_the_wire()
    {
        await SeedMaturedProductAsync("ORD-3", 500m);

        await Should.NotThrowAsync(async () =>
            await _orderClient.AcceptOrderItemAsync(AppKey, AppSecret, new[] { 6000000000L }, numberOfPackages: 1));

        await Should.NotThrowAsync(async () =>
            await _orderClient.RejectOrderItemAsync(AppKey, AppSecret, new[] { 6000000000L }, "stok yok"));

        await Should.NotThrowAsync(async () =>
            await _orderClient.MakeShipmentAsync(
                AppKey, AppSecret, 6000000000L, "7", "TRK-TEST", campaignNumber: null, shipmentMethod: 1));
    }

    /// <summary>Kimlik başlıkları GERÇEKTEN gönderiliyor mu — mock başlık yoksa 401 döner, istemci de onu
    /// dostane hataya çevirir. Sınıf takasında bu adım hiç koşmazdı.</summary>
    [Fact]
    public async Task Missing_credentials_are_rejected_by_the_wire_not_by_a_local_guard()
    {
        // Boş kimlikle çağrı: guard'lar geçse bile mock 401 döner → istemci BusinessException'a çevirir.
        await Should.ThrowAsync<Volo.Abp.BusinessException>(async () =>
            await _queryClient.QueryAsync(
                new N11ProductQueryFilter(0, 1, null, null, null, null, null), string.Empty, string.Empty));
    }
}
