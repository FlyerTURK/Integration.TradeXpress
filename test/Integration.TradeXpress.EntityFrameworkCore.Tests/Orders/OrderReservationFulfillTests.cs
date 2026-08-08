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
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// REZERVE → FİZİKİ ÇIKIŞ — state machine'in dönüşü olmayan ayağı.
///
/// <para><b>Kapatılan açık:</b> <c>MarkFulfilled</c> ve <c>OrderFulfillmentLinkKind.PhysicalExit</c> üretim
/// kodunda SIFIR çağırana sahipti. Rezerve edilen mal fiilen çıkarılabiliyordu ama sistem bunu bilmiyordu:
/// rezervasyon sonsuza kadar "Rezerve"de kalıyor, stok iki farklı gerçeği aynı anda anlatıyordu.</para>
///
/// <para><b>EN KRİTİK TEST — çift sayım.</b> Fiziki çıkış satırı yazılıp rezervasyon satırı DÜŞÜRÜLMEZSE aynı
/// mal iki kez eksilir: <c>Available</c> 30 yerine 10 çıkar ve ürün stokta olduğu hâlde satıştan kalkar.
/// Hata sessizdir — hiçbir istisna doğmaz, yalnız kanal adedi sebepsizce küçülür. Bu testin varlık sebebi odur.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class OrderReservationFulfillTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string ProductEntityName = "Product";
    private const string RemoteLineId = "FL-1";

    private readonly IOrderAppService _orderAppService;
    private readonly OrderReservationManager _reservationManager;
    private readonly IMetalReportAppService _metalReport;
    private readonly IVoucherAppService _voucherAppService;
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

    public OrderReservationFulfillTests()
    {
        _orderAppService    = GetRequiredService<IOrderAppService>();
        _reservationManager = GetRequiredService<OrderReservationManager>();
        _metalReport        = GetRequiredService<IMetalReportAppService>();
        _voucherAppService  = GetRequiredService<IVoucherAppService>();
        _orders             = GetRequiredService<IRepository<Order, Guid>>();
        _orderLines         = GetRequiredService<IRepository<OrderLine, Guid>>();
        _operationalLines   = GetRequiredService<IRepository<OrderLineOperationalData, Guid>>();
        _reservations       = GetRequiredService<IRepository<OrderReservation, Guid>>();
        _links              = GetRequiredService<IRepository<OrderFulfillmentLink, Guid>>();
        _channels           = GetRequiredService<IRepository<SalesChannelTrN11, Guid>>();
        _products           = GetRequiredService<IRepository<Product, Guid>>();
        _variants           = GetRequiredService<IRepository<EntityVariant, Guid>>();
        _recipeLines        = GetRequiredService<IRepository<ProductVariantRecipeLine, Guid>>();
        _metals             = GetRequiredService<IRepository<Metal, Guid>>();
        _seeder             = GetRequiredService<VoucherTestDataSeeder>();
        _companyContext     = GetRequiredService<TestCompanyContextProvider>();
    }

    /// <summary>① ÇİFT SAYIM YOK: 50 giriş → 20 rezerve → fiziki çıkış → <c>Net=30</c>, <c>ReservedOut=0</c>,
    /// <c>Available=30</c>. <b>10 DEĞİL.</b></summary>
    [Fact]
    public async Task Fulfillment_moves_stock_without_double_counting()
    {
        var scenario = await SeedAsync("FUL", recipeGramsPerUnit: 20m);

        await WithUnitOfWorkAsync(
            () => _reservationManager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));

        var reserved = await StockAsync(scenario);
        reserved.NetAmount.ShouldBe(50m);
        reserved.ReservedOutAmount.ShouldBe(20m);
        reserved.AvailableAmount.ShouldBe(30m);

        await _orderAppService.FulfillReservationAsync(new OrderFulfillmentInputDto
        {
            OrderId  = scenario.OrderId,
            BranchId = scenario.BranchId,
            VaultId  = scenario.VaultId,
            Note     = "Mal hazırlandı, çıkış yapıldı.",
        });

        var after = await StockAsync(scenario);
        after.NetAmount.ShouldBe(30m);            // 50 − 20 fiziki çıkış
        after.ReservedOutAmount.ShouldBe(0m);     // rezervasyon düştü
        after.AvailableAmount.ShouldBe(30m);      // ⚠ 10 DEĞİL — çift sayımın pini
    }

    /// <summary>② Fiziki çıkış BAĞ kaydı üretir — hangi çıkış satırı hangi kalemi karşıladı.</summary>
    [Fact]
    public async Task Fulfillment_records_a_physical_exit_link()
    {
        var scenario = await SeedAsync("FLK", recipeGramsPerUnit: 10m);

        await WithUnitOfWorkAsync(
            () => _reservationManager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));

        await _orderAppService.FulfillReservationAsync(new OrderFulfillmentInputDto
        {
            OrderId = scenario.OrderId, BranchId = scenario.BranchId, VaultId = scenario.VaultId,
        });

        var links = await WithUnitOfWorkAsync(() => _links.GetListAsync(l => l.OrderId == scenario.OrderId));

        links.ShouldContain(l => l.Kind == OrderFulfillmentLinkKind.Reservation);
        var exit = links.Single(l => l.Kind == OrderFulfillmentLinkKind.PhysicalExit);
        exit.RemoteLineId.ShouldBe(RemoteLineId);
        exit.FulfilledAmount.ShouldBe(10m);

        // Fiyat farkı BEYAN EDİLMEDİ → null. 0 olsaydı "fark yok" beyanı sayılırdı; ikisi farklı bilgidir.
        exit.PriceDifference.ShouldBeNull();
    }

    /// <summary>③ DÖNÜŞÜ OLMAYAN NOKTA: karşılanmış rezervasyon serbest bırakılamaz.
    /// <para>Bırakılabilseydi stok İKİ KEZ geri verilirdi — mal çıkmış olduğu hâlde.</para></summary>
    [Fact]
    public async Task Fulfilled_reservation_cannot_be_released()
    {
        var scenario = await SeedAsync("FRL", recipeGramsPerUnit: 5m);

        await WithUnitOfWorkAsync(
            () => _reservationManager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));
        await _orderAppService.FulfillReservationAsync(new OrderFulfillmentInputDto
        {
            OrderId = scenario.OrderId, BranchId = scenario.BranchId, VaultId = scenario.VaultId,
        });

        (await Should.ThrowAsync<BusinessException>(
            () => _orderAppService.ReleaseReservationAsync(new OrderReservationReleaseDto
            {
                OrderId = scenario.OrderId, Reason = "geri al",
            })))
            .Code.ShouldBe("TradeXpress:OrderReservation:CannotReleaseFulfilled");
    }

    /// <summary>④ Karşılanmış rezervasyonda İPTAL ONAYI bloklanır — artık iade sürecidir.</summary>
    [Fact]
    public async Task Cancellation_cannot_be_approved_after_fulfillment()
    {
        var scenario = await SeedAsync("FCA", recipeGramsPerUnit: 5m);

        await WithUnitOfWorkAsync(
            () => _reservationManager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));
        await _orderAppService.FulfillReservationAsync(new OrderFulfillmentInputDto
        {
            OrderId = scenario.OrderId, BranchId = scenario.BranchId, VaultId = scenario.VaultId,
        });

        (await Should.ThrowAsync<BusinessException>(
            () => _orderAppService.DecideCancellationAsync(new OrderCancellationDecisionDto
            {
                OrderId = scenario.OrderId, Approve = true,
            })))
            .Code.ShouldBe("TradeXpress:OrderReservation:AlreadyFulfilled");
    }

    /// <summary>⑤ Karşılanmış sipariş senkron döngüsünde DİRİLTİLMEZ — ikinci bir rezervasyon fişi doğmaz.</summary>
    [Fact]
    public async Task Sync_does_not_resurrect_a_fulfilled_reservation()
    {
        var scenario = await SeedAsync("FSY", recipeGramsPerUnit: 8m);

        await WithUnitOfWorkAsync(
            () => _reservationManager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));
        await _orderAppService.FulfillReservationAsync(new OrderFulfillmentInputDto
        {
            OrderId = scenario.OrderId, BranchId = scenario.BranchId, VaultId = scenario.VaultId,
        });

        var again = await WithUnitOfWorkAsync(
            () => _reservationManager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));

        again!.Status.ShouldBe(OrderReservationStatus.Fulfilled);
        (await StockAsync(scenario)).ReservedOutAmount.ShouldBe(0m);
    }

    /// <summary>⑥ Rezerve DEĞİLKEN fiziki çıkış REDDEDİLİR — sırayı atlayarak fiş yazılamaz.</summary>
    [Fact]
    public async Task Fulfillment_requires_a_reserved_reservation()
    {
        var scenario = await SeedAsync("FGD", recipeGramsPerUnit: 5m, matchLine: false);

        // Eşleşme yok → rezervasyon Blocked kalır.
        await WithUnitOfWorkAsync(
            () => _reservationManager.EnsureReservationAsync(scenario.CompanyId, scenario.OrderId));

        (await Should.ThrowAsync<BusinessException>(
            () => _orderAppService.FulfillReservationAsync(new OrderFulfillmentInputDto
            {
                OrderId = scenario.OrderId, BranchId = scenario.BranchId, VaultId = scenario.VaultId,
            })))
            .Code.ShouldBe("TradeXpress:OrderReservation:MustBeReservedToFulfill");
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    private async Task<MetalStockRowDto> StockAsync(FulfillScenario scenario)
    {
        var rows = await _metalReport.GetStockAsync(new MetalReportFilterDto());
        return rows.Single(r => r.MetalId == scenario.MetalId);
    }

    /// <summary>50 gr stoklu maden + o madeni tüketen reçeteli ürün + eşleşmiş sipariş kalemi
    /// (<c>OrderReservationManagerTests</c> senaryosunun ikizi).</summary>
    private async Task<FulfillScenario> SeedAsync(string prefix, decimal recipeGramsPerUnit, bool matchLine = true)
    {
        var data = await WithUnitOfWorkAsync(() => _seeder.SeedCompanyGraphAsync(prefix));
        _companyContext.CompanyId = data.CompanyId;

        var metalId = await WithUnitOfWorkAsync(async () =>
        {
            var metal = new Metal($"{prefix}-MTL", $"{prefix} Maden", data.HasUnitId, data.CompanyId);
            await _metals.InsertAsync(metal, autoSave: true);
            return metal.Id;
        });

        await _voucherAppService.SaveLineAsync(new VoucherLineDto
        {
            BranchId = data.BranchId, VaultId = data.VaultId,
            AccountId = data.AccountId, SubAccountId = data.SubAccountId,
            Type = ProcessType.Metal, Direction = ProcessDirectionType.Inbound,
            PaymentType = ProcessPaymentType.Normal,
            CommodityId = metalId, CommodityCode = $"{prefix}-MTL",
            Quantity = 5m, Amount = 50m, Factor = 1m, Total = 50m, MainUnitId = data.HasUnitId,
        });

        var orderId = await WithUnitOfWorkAsync(async () =>
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
                RemoteLineId: RemoteLineId, Barcode: null, StockCode: $"{prefix}-SKU",
                ProductNameSnapshot: $"{prefix} Ürünü", Quantity: 1m, UnitPrice: 100m, LineTotal: 100m,
                RemoteLineStatus: null, ProductVariantId: null));
            await _orderLines.InsertAsync(line, autoSave: true);

            if (matchLine)
            {
                var operational = new OrderLineOperationalData(data.CompanyId, order.Id, RemoteLineId);
                operational.SetProductMatch(variant.Id, $"{prefix} Varyant", null, DateTime.UtcNow);
                await _operationalLines.InsertAsync(operational, autoSave: true);
            }

            return order.Id;
        });

        return new FulfillScenario(data.CompanyId, orderId, metalId, data.BranchId, data.VaultId);
    }

    private sealed record FulfillScenario(Guid CompanyId, Guid OrderId, Guid MetalId, Guid BranchId, Guid VaultId);
}
