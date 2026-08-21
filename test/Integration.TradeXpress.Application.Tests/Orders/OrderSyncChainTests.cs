using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// SİPARİŞ SENKRON ZİNCİRİ — seed ↔ delta ayrımı, iptal bildirimi (<c>NotifyCancellationRequestedAsync</c>), idempotens.
///
/// <para><b>Neden bu testler:</b> <c>OrderSyncManager</c>'ın N11 dalını koşan SIFIR test vardı. Zincirin
/// tamamı (çekim → satır yazımı → ürün eşleştirme → rezervasyon → iptal bildirimi) yalnız canlıda çalışıyordu ve
/// her halkası sessiz başarısızlığa açıktı.</para>
///
/// <para><b>Sahte istemci ÇAĞRILARI KAYDEDER:</b> seed ile delta aynı siparişleri döndürdüğü için, iki kolun
/// karışıp karışmadığı ancak "hangi pencereyle istendi?" sorusundan anlaşılır. Dönen veriye bakan bir test
/// ikisini ayırt edemezdi.</para>
/// </summary>
public abstract class OrderSyncChainTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly OrderSyncManager _syncManager;
    private readonly FakeN11OrderClient _fakeN11;
    private readonly IRepository<SalesChannelTrN11, Guid> _channels;
    private readonly IRepository<Order, Guid> _orders;
    private readonly IRepository<OrderReservation, Guid> _reservations;
    private readonly IOrderAppService _orderAppService;
    private readonly IRepository<Products.Product, Guid> _products;
    private readonly IRepository<Variants.EntityVariant, Guid> _variants;
    private readonly ICurrentCompany _currentCompany;

    protected OrderSyncChainTests()
    {
        _syncManager    = GetRequiredService<OrderSyncManager>();
        _fakeN11        = GetRequiredService<FakeN11OrderClient>();
        _channels       = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _orders         = GetRequiredService<IRepository<Order, Guid>>();
        _reservations   = GetRequiredService<IRepository<OrderReservation, Guid>>();
        _orderAppService = GetRequiredService<IOrderAppService>();
        _products        = GetRequiredService<IRepository<Products.Product, Guid>>();
        _variants        = GetRequiredService<IRepository<Variants.EntityVariant, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();

        _fakeN11.Reset();
    }

    /// <summary>① SEED tarih filtresi GÖNDERMEZ — period gönderilseydi kanalın geçmişi gizlenirdi.</summary>
    [Fact]
    public async Task Seed_arm_requests_without_a_date_window()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "SEED");
            _fakeN11.RemoteOrders.Add(BuildOrder("N-1", "1001", "1", 100m));

            await WithUnitOfWorkAsync(() => _syncManager.SyncSingleChannelAsync(companyId, channel.Id, new OrderFetchResultDto()));

            _fakeN11.ListCalls.ShouldNotBeEmpty();
            _fakeN11.ListCalls.ShouldAllBe(c => c.SinceUtc == null);
        }
    }

    /// <summary>② EŞLEŞMEYEN kalemli sipariş SESSİZ GEÇİLMEZ — <c>Blocked</c> rezervasyon kaydı alır.
    /// <para>Eski davranış (sessizce atlamak) rezervasyon eklendikten sonra çok daha tehlikeliydi: kullanıcı
    /// siparişin rezerve edildiğini sanardı.</para></summary>
    [Fact]
    public async Task Unmatched_order_produces_a_blocked_reservation_not_silence()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "BLOK");
            _fakeN11.RemoteOrders.Add(BuildOrder("N-2", "1002", "1", 250m));

            await WithUnitOfWorkAsync(() => _syncManager.SyncSingleChannelAsync(companyId, channel.Id, new OrderFetchResultDto()));

            var order = await FindOrderAsync(companyId, "N-2");
            var reservation = await WithUnitOfWorkAsync(
                () => _reservations.FirstOrDefaultAsync(r => r.OrderId == order.Id));

            reservation.ShouldNotBeNull();
            reservation!.Status.ShouldBe(OrderReservationStatus.Blocked);
            reservation.Note.ShouldNotBeNullOrWhiteSpace();
        }
    }

    /// <summary>③ İKİNCİ tur İDEMPOTENT — yeni sipariş doğmaz, ikinci rezervasyon kaydı açılmaz.</summary>
    [Fact]
    public async Task Second_round_is_idempotent()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IDEM");
            _fakeN11.RemoteOrders.Add(BuildOrder("N-3", "1003", "1", 90m));

            await WithUnitOfWorkAsync(() => _syncManager.SyncSingleChannelAsync(companyId, channel.Id, new OrderFetchResultDto()));
            await WithUnitOfWorkAsync(() => _syncManager.SyncSingleChannelAsync(companyId, channel.Id, new OrderFetchResultDto()));

            var orders = await WithUnitOfWorkAsync(
                () => _orders.GetListAsync(o => o.CompanyId == companyId && o.RemoteOrderId == "N-3"));
            orders.Count.ShouldBe(1);

            var reservations = await WithUnitOfWorkAsync(
                () => _reservations.GetListAsync(r => r.OrderId == orders[0].Id));
            reservations.Count.ShouldBe(1);
        }
    }

    /// <summary>④ TERMİNAL sipariş rezervasyon kaydı DOĞURMAZ — canlıdaki 106 teslim edilmiş siparişin
    /// "silahlı tuzak" olmaktan çıkışının senkron yolundan pini.</summary>
    [Fact]
    public async Task Delivered_order_creates_no_reservation_record()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "TESL");
            // N11 order-status 5 = Tamamlandı → nötr Delivered.
            _fakeN11.RemoteOrders.Add(BuildOrder("N-4", "1004", "5", 120m));

            await WithUnitOfWorkAsync(() => _syncManager.SyncSingleChannelAsync(companyId, channel.Id, new OrderFetchResultDto()));

            var order = await FindOrderAsync(companyId, "N-4");
            order.NeutralStatus.ShouldBe(OrderStatus.Delivered);

            (await WithUnitOfWorkAsync(() => _reservations.GetListAsync(r => r.OrderId == order.Id)))
                .ShouldBeEmpty();
        }
    }

    /// <summary>⑤ İPTAL edilmiş sipariş çekilirse rezervasyon KURULMAZ (terminal guard'ı) —
    /// <c>NotifyCancellationRequestedAsync</c> de kayıt UYDURMAZ. İptal kararı ancak ZATEN rezerve edilmiş bir siparişte anlamlıdır.</summary>
    [Fact]
    public async Task Cancelled_order_neither_reserves_nor_invents_a_decision()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "IPTL");
            // N11 order-status 3 = İptal → nötr Cancelled.
            _fakeN11.RemoteOrders.Add(BuildOrder("N-5", "1005", "3", 75m));

            await WithUnitOfWorkAsync(() => _syncManager.SyncSingleChannelAsync(companyId, channel.Id, new OrderFetchResultDto()));

            var order = await FindOrderAsync(companyId, "N-5");
            order.NeutralStatus.ShouldBe(OrderStatus.Cancelled);

            (await WithUnitOfWorkAsync(() => _reservations.GetListAsync(r => r.OrderId == order.Id)))
                .ShouldBeEmpty();
        }
    }

    /// <summary>⑥ Eşleştirme adayları YALNIZ çalışılan şirketin ürünlerini döndürür.
    /// <para>Aday listesi kullanıcının GÖRDÜĞÜ bir ekrandır: yabancı şirketin ürün adları buradan sızarsa
    /// hiçbir hata oluşmaz, yalnız görülmemesi gereken veri görünür.</para></summary>
    [Fact]
    public async Task Match_candidates_are_scoped_to_the_working_company()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();

        await SeedProductAsync(mine, "BENIM");
        await SeedProductAsync(other, "YABANCI");

        using (_currentCompany.Change(mine))
        {
            var candidates = await WithUnitOfWorkAsync(
                () => _orderAppService.GetLineMatchCandidatesAsync(new OrderLineMatchCandidateRequestDto()));

            candidates.ShouldNotBeEmpty();
            candidates.ShouldAllBe(c => c.ProductCode.StartsWith("BENIM"));
        }
    }

    /// <summary>⑦ Arama metni ürün KODU, ADI ve VARYANT kodunda arar — kullanıcı elindeki pazaryeri stok
    /// koduna neyin benzediğini arar, hangi alanda tutulduğunu önceden bilemez.</summary>
    [Fact]
    public async Task Match_candidate_search_covers_code_name_and_variant()
    {
        var companyId = Guid.NewGuid();
        await SeedProductAsync(companyId, "ARAMA");

        using (_currentCompany.Change(companyId))
        {
            var byCode = await WithUnitOfWorkAsync(
                () => _orderAppService.GetLineMatchCandidatesAsync(new OrderLineMatchCandidateRequestDto { Search = "ARAMA" }));
            byCode.ShouldNotBeEmpty();

            var byVariant = await WithUnitOfWorkAsync(
                () => _orderAppService.GetLineMatchCandidatesAsync(new OrderLineMatchCandidateRequestDto { Search = "-V1" }));
            byVariant.ShouldNotBeEmpty();

            var nonsense = await WithUnitOfWorkAsync(
                () => _orderAppService.GetLineMatchCandidatesAsync(new OrderLineMatchCandidateRequestDto { Search = "ZZZ-YOK" }));
            nonsense.ShouldBeEmpty();
        }
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    private async Task SeedProductAsync(Guid companyId, string prefix)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var product = new Products.Product(companyId, $"{prefix}-URN", $"{prefix} Ürünü");
            await _products.InsertAsync(product, autoSave: true);

            var variant = new Variants.EntityVariant(
                companyId, "Product", product.Id, $"{prefix}-V1", $"{prefix} Varyant", isMain: true);
            await _variants.InsertAsync(variant, autoSave: true);
        });
    }

    private async Task<Order> FindOrderAsync(Guid companyId, string remoteId)
    {
        var order = await WithUnitOfWorkAsync(
            () => _orders.FirstOrDefaultAsync(o => o.CompanyId == companyId && o.RemoteOrderId == remoteId));
        order.ShouldNotBeNull($"Sipariş {remoteId} yazılmadı — senkron zinciri kopmuş.");
        return order!;
    }

    private async Task<SalesChannelTrN11> SeedChannelAsync(Guid companyId, string suffix)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _channels.InsertAsync(
                new SalesChannelTrN11(companyId, $"N11-{suffix}", $"N11 {suffix}", "app-key", "app-secret"),
                autoSave: true));
    }

    private static RemoteOrder BuildOrder(string remoteId, string orderNumber, string remoteStatus, decimal amount)
    {
        return new RemoteOrder(
            RemoteOrderId: remoteId,
            OrderNumber: orderNumber,
            OrderDate: DateTime.UtcNow,
            RemoteStatus: remoteStatus,
            CustomerName: "Test Müşteri",
            TotalAmount: amount,
            CargoProvider: "Test Kargo",
            CargoTrackingNumber: "TRK-1",
            Lines: new List<RemoteOrderLine>
            {
                new(RemoteLineId: $"{remoteId}-L1",
                    Barcode: null,
                    StockCode: "ESLESMEYEN-SKU",
                    ProductName: "Test Ürün",
                    Quantity: 1m,
                    UnitPrice: amount,
                    LineTotal: amount,
                    RemoteLineStatus: "1"),
            });
    }
}
