using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Reports;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// SİPARİŞ REZERVASYONU uçtan uca (Faz 7) — <see cref="OrderReservationManager"/>.
///
/// <para>Kapatılan delik: sipariş bugüne kadar stoğa HİÇ dokunmuyordu; sipariş ile fiş arası boyunca aynı
/// maden hem satılmış hem satılabilir görünüyordu.</para>
///
/// <para><b>Neden entegrasyon testi:</b> zincir katmanlar arası — eşleşmiş kalem → varyantın reçetesi →
/// merkez şube/kasa → kanalın carisi → fiş → stok raporu. Herhangi bir halkanın sessizce kopması
/// "rezerve edildi" yalanı üretir; birim testi bunu göremezdi.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class OrderReservationManagerTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string ProductEntityName = "Product";
    private const string RemoteLineId = "RL-1";

    private readonly OrderReservationManager _manager;
    private readonly IMetalReportAppService _metalReport;
    private readonly IRepository<Order, Guid> _orders;
    private readonly IRepository<OrderLine, Guid> _orderLines;
    private readonly IRepository<OrderLineOperationalData, Guid> _operationalLines;
    private readonly IRepository<OrderReservation, Guid> _reservations;
    private readonly IRepository<OrderFulfillmentLink, Guid> _links;
    private readonly IRepository<SalesChannelTrN11, Guid> _channels;
    private readonly IRepository<Product, Guid> _products;
    private readonly IRepository<EntityVariant, Guid> _variants;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLines;
    private readonly IRepository<Metal, Guid> _metals;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly TestCompanyContextProvider _companyContext;

    public OrderReservationManagerTests()
    {
        _manager          = GetRequiredService<OrderReservationManager>();
        _metalReport      = GetRequiredService<IMetalReportAppService>();
        _orders           = GetRequiredService<IRepository<Order, Guid>>();
        _orderLines       = GetRequiredService<IRepository<OrderLine, Guid>>();
        _operationalLines = GetRequiredService<IRepository<OrderLineOperationalData, Guid>>();
        _reservations     = GetRequiredService<IRepository<OrderReservation, Guid>>();
        _links            = GetRequiredService<IRepository<OrderFulfillmentLink, Guid>>();
        _channels         = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _products         = GetRequiredService<IRepository<Product, Guid>>();
        _variants         = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _recipeLines      = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
        _metals           = GetRequiredService<IRepository<Metal, Guid>>();
        _seeder           = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext   = GetRequiredService<TestCompanyContextProvider>();
    }

    /// <summary>ASIL AKIŞ: reçeteli bir kalem çekilince maden RESERVE edilir ve KULLANILABİLİR stok düşer —
    /// fiziksel Net'e DOKUNULMADAN.</summary>
    [Fact]
    public async Task Reservation_reduces_available_stock_without_touching_physical_net()
    {
        var scenario = await SeedAsync("ORV", recipeGramsPerUnit: 8m, orderedQuantity: 2m);

        var reservation = await WithUnitOfWorkAsync(
            () => _manager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));

        reservation.Status.ShouldBe(OrderReservationStatus.Reserved);
        reservation.VoucherId.ShouldNotBeNull();

        var row = await StockAsync(scenario);
        row.NetAmount.ShouldBe(50m);              // fiziksel giriş DEĞİŞMEDİ
        row.ReservedOutAmount.ShouldBe(16m);      // 8 gr/adet × 2 adet
        row.AvailableAmount.ShouldBe(34m);        // 50 − 16

        // Bağ kaydı: hangi fiş satırı hangi sipariş kalemini karşılıyor (birleştirme senaryosunun temeli).
        var links = await WithUnitOfWorkAsync(() => _links.GetListAsync(l => l.OrderId == scenario.OrderId));
        var link = links.ShouldHaveSingleItem();
        link.Kind.ShouldBe(OrderFulfillmentLinkKind.Reservation);
        link.RemoteLineId.ShouldBe(RemoteLineId);
        link.FulfilledAmount.ShouldBe(16m);

        // Fiyat farkı BEYAN EDİLMEDİ — 0 değil null (ikisi farklı bilgidir).
        link.PriceDifference.ShouldBeNull();
    }

    /// <summary>İDEMPOTENT: worker 2 dakikada bir aynı siparişle döner. İkinci çağrı yeni fiş AÇMAZ —
    /// açsaydı stok her turda biraz daha erirdi ve kimse sebebini bulamazdı.</summary>
    [Fact]
    public async Task Second_call_does_not_create_a_second_reservation()
    {
        var scenario = await SeedAsync("ORI", recipeGramsPerUnit: 5m, orderedQuantity: 1m);

        var first = await WithUnitOfWorkAsync(
            () => _manager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));
        var second = await WithUnitOfWorkAsync(
            () => _manager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));

        second.VoucherId.ShouldBe(first.VoucherId);

        var row = await StockAsync(scenario);
        row.ReservedOutAmount.ShouldBe(5m);       // 10 DEĞİL

        var links = await WithUnitOfWorkAsync(() => _links.GetListAsync(l => l.OrderId == scenario.OrderId));
        links.Count.ShouldBe(1);
    }

    /// <summary>KOŞULSUZ rezervasyon (2026-08-05 karar #9): stok yetmese bile yazılır ve kullanılabilir
    /// EKSİYE düşer — <i>"hata yapmışsak cezasını biz çekeriz ki tutarlılık sürsün"</i>. Kırpma KANAL
    /// sınırında yapılır, defterde değil.</summary>
    [Fact]
    public async Task Reservation_is_written_even_when_stock_is_insufficient()
    {
        var scenario = await SeedAsync("ORN", recipeGramsPerUnit: 40m, orderedQuantity: 2m);   // 80 gr ihtiyaç

        var reservation = await WithUnitOfWorkAsync(
            () => _manager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));

        reservation.Status.ShouldBe(OrderReservationStatus.Reserved);

        var row = await StockAsync(scenario);
        row.NetAmount.ShouldBe(50m);
        row.ReservedOutAmount.ShouldBe(80m);
        row.AvailableAmount.ShouldBe(-30m);       // EKSİ — defter dürüst kalır
    }

    /// <summary>Eşleşmemiş kalem SESSİZ ATLANMAZ: rezervasyon <c>Blocked</c> gerekçesiyle kaydedilir.
    /// <para>Eski davranış (sessizce geçmek) rezervasyon eklendikten sonra çok daha tehlikeli olurdu —
    /// kullanıcı siparişin rezerve edildiğini sanardı.</para></summary>
    [Fact]
    public async Task Unmatched_order_line_is_recorded_as_blocked_not_skipped()
    {
        var scenario = await SeedAsync("ORB", recipeGramsPerUnit: 5m, orderedQuantity: 1m, matchLine: false);

        var reservation = await WithUnitOfWorkAsync(
            () => _manager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));

        reservation.Status.ShouldBe(OrderReservationStatus.Blocked);
        reservation.VoucherId.ShouldBeNull();
        reservation.Note.ShouldNotBeNullOrWhiteSpace();

        var row = await StockAsync(scenario);
        row.ReservedOutAmount.ShouldBe(0m);
    }

    /// <summary>Serbest bırakma fiş satırlarını soft-delete eder → sayaçtan düşer, denetim izi kalır.
    /// Serbest bırakılan rezervasyon senkron döngüsünde YENİDEN kurulmaz (kullanıcının kararı sessizce
    /// geri alınamaz).</summary>
    [Fact]
    public async Task Release_returns_stock_and_is_not_resurrected_by_the_next_sync()
    {
        var scenario = await SeedAsync("ORR", recipeGramsPerUnit: 12m, orderedQuantity: 1m);

        await WithUnitOfWorkAsync(() => _manager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));
        (await StockAsync(scenario)).AvailableAmount.ShouldBe(38m);

        await WithUnitOfWorkAsync(() => _manager.ReleaseAsync(scenario.OrderId, "İptal onaylandı."));

        var released = await StockAsync(scenario);
        released.ReservedOutAmount.ShouldBe(0m);
        released.AvailableAmount.ShouldBe(50m);

        // Senkron döngüsü geri gelirse: diriltme YOK.
        var again = await WithUnitOfWorkAsync(
            () => _manager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));
        again.Status.ShouldBe(OrderReservationStatus.Released);
        (await StockAsync(scenario)).ReservedOutAmount.ShouldBe(0m);
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    private async Task<MetalStockRowDto> StockAsync(ReservationScenario scenario)
    {
        var rows = await _metalReport.GetStockAsync(new MetalReportFilterDto());
        return rows.Single(r => r.MetalId == scenario.MetalId);
    }

    /// <summary>Senaryo: 50 gr stoklu bir maden + o madeni tüketen reçeteli ürün + eşleşmiş sipariş kalemi.</summary>
    private async Task<ReservationScenario> SeedAsync(
        string prefix, decimal recipeGramsPerUnit, decimal orderedQuantity, bool matchLine = true)
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync(prefix));
        _companyContext.CompanyId = data.CompanyId;

        // GERÇEK katalog kaydı: rezervasyon fişi emtia KODUNU katalogtan çözer ve stok raporu
        // (emtia, kod, varyant, birim) ile gruplar — kayıt olmasaydı kod boş kalır ve rapor aynı madeni
        // İKİ satırda gösterirdi (fixture değil, gruplamanın gerçek davranışı).
        var metalId = await WithUnitOfWorkAsync(async () =>
        {
            var metal = new Metal($"{prefix}-MTL", $"{prefix} Maden", data.HasUnitId, data.CompanyId);
            await _metals.InsertAsync(metal, autoSave: true);
            return metal.Id;
        });

        // Fiziksel stok: 50 gr giriş (fişi normal yoldan yazıyoruz — rapor aynı kaynağı okur).
        var voucherAppService = GetRequiredService<IVoucherAppService>();
        await voucherAppService.SaveLineAsync(new VoucherLineDto
        {
            BranchId      = data.BranchId,
            VaultId       = data.VaultId,
            AccountId     = data.AccountId,
            SubAccountId  = data.SubAccountId,
            Type          = ProcessType.Metal,
            Direction     = ProcessDirectionType.Inbound,
            PaymentType   = ProcessPaymentType.Normal,
            CommodityId   = metalId,
            CommodityCode = $"{prefix}-MTL",
            Quantity      = 5m,
            Amount        = 50m,
            Factor        = 1m,
            Total         = 50m,
            MainUnitId    = data.HasUnitId,
        });

        return await WithUnitOfWorkAsync(async () =>
        {
            var channel = new SalesChannelTrN11(data.CompanyId, $"{prefix}-CH", $"{prefix} Kanal", "key", "secret");
            channel.SetSubAccount(data.SubAccountId);
            await _channels.InsertAsync(channel, autoSave: true);

            var product = new Product(data.CompanyId, $"{prefix}-URN", $"{prefix} Ürünü");
            product.SetStockPolicy(ProductStockPolicy.Calculated);
            await _products.InsertAsync(product, autoSave: true);

            var variant = new EntityVariant(
                data.CompanyId, ProductEntityName, product.Id, $"{prefix}-V1", $"{prefix} Varyant", isMain: true);
            await _variants.InsertAsync(variant, autoSave: true);

            var recipeLine = new ProductVariantRecipeLine(
                data.CompanyId, variant.Id, RecipeComponentType.CatalogCommodity, lineOrder: 0);
            recipeLine.SetCatalogCommodity(
                ProcessType.Metal, metalId, commodityVariantId: null,
                quantity: 0m, amount: recipeGramsPerUnit, factor: 1m, valuationUnitId: data.HasUnitId,
                ProcessPaymentType.Normal, payFactor: 0m, payUnitId: null);
            await _recipeLines.InsertAsync(recipeLine, autoSave: true);

            var order = new Order(
                data.CompanyId, channel.Id, SalesChannelType.TrN11, $"{prefix}-REMOTE", $"{prefix}-001");
            await _orders.InsertAsync(order, autoSave: true);

            var line = new OrderLine(data.CompanyId, order.Id, new OrderLineSnapshot(
                RemoteLineId: RemoteLineId,
                Barcode: null,
                StockCode: $"{prefix}-SKU",
                ProductNameSnapshot: $"{prefix} Ürünü",
                Quantity: orderedQuantity,
                UnitPrice: 100m,
                LineTotal: 100m * orderedQuantity,
                RemoteLineStatus: null,
                ProductVariantId: null));
            await _orderLines.InsertAsync(line, autoSave: true);

            if (matchLine)
            {
                var operational = new OrderLineOperationalData(data.CompanyId, order.Id, RemoteLineId);
                operational.SetProductMatch(variant.Id, $"{prefix} Varyant", null, DateTime.UtcNow);
                await _operationalLines.InsertAsync(operational, autoSave: true);
            }

            return new ReservationScenario(data.CompanyId, order.Id, metalId, data.BranchId);
        });
    }

    private sealed record ReservationScenario(Guid CompanyId, Guid OrderId, Guid MetalId, Guid BranchId);
}
