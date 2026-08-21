using System;
using System.Collections.Generic;
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
/// İADE GİRİŞİ — stok yalnız mal KASAYA GİRDİĞİNDE döner.
///
/// <para><b>Kural (2026-08-05 Hakan kararı):</b> "iade mal fiziksel olarak kasaya GİRENE kadar stokta yok
/// sayılır". Kanaldaki "iade talep edildi" / "kargoda iade" statüleri stoğa dokunmaz — mal elimize geçmeden
/// satılabilir göstermek, müşterinin onu ikinci kez satın alabilmesi demektir.</para>
///
/// <para><b>Çift sayım guard'ı:</b> iade rezervasyonu DİRİLTMEZ. Diriltseydi stok iki kez artardı — bir kez
/// giriş fişiyle, bir kez de rezervasyonun serbest kalmasıyla.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class OrderReturnEntryTests : TradeXpressEntityFrameworkCoreTestBase
{
    private const string ProductEntityName = "Product";
    private const string RemoteLineId = "RT-1";

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

    public OrderReturnEntryTests()
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

    /// <summary>① İade girişi stoğu GERİ VERİR; rezervasyon <c>Fulfilled</c> KALIR (dirilme yok).</summary>
    [Fact]
    public async Task Return_entry_restores_stock_without_resurrecting_the_reservation()
    {
        var scenario = await SeedFulfilledAsync("RET", grams: 20m);

        var afterExit = await StockAsync(scenario);
        afterExit.NetAmount.ShouldBe(30m);   // 50 − 20 çıkış

        var exitLink = await ExitLinkAsync(scenario.OrderId);

        var result = await _orderAppService.RegisterReturnEntryAsync(new OrderReturnEntryDto
        {
            OrderId = scenario.OrderId, BranchId = scenario.BranchId, VaultId = scenario.VaultId,
            Note = "Müşteri iade etti, mal teslim alındı.",
            Lines = new List<OrderReturnEntryLineDto>
            {
                new() { PhysicalExitLinkId = exitLink.Id, Quantity = exitLink.FulfilledQuantity, Amount = 20m },
            },
        });

        result.RegisteredLines.ShouldBe(1);
        result.Issues.ShouldBeEmpty();

        var afterReturn = await StockAsync(scenario);
        afterReturn.NetAmount.ShouldBe(50m);          // mal geri geldi
        afterReturn.ReservedOutAmount.ShouldBe(0m);   // ⚠ rezervasyon DİRİLMEDİ
        afterReturn.AvailableAmount.ShouldBe(50m);

        var reservation = await WithUnitOfWorkAsync(
            () => _reservations.FirstOrDefaultAsync(r => r.OrderId == scenario.OrderId));
        reservation!.Status.ShouldBe(OrderReservationStatus.Fulfilled);
    }

    /// <summary>② İade bağı <c>Return</c> türünde yazılır ve miktarı taşır.</summary>
    [Fact]
    public async Task Return_entry_records_a_return_link()
    {
        var scenario = await SeedFulfilledAsync("RTL", grams: 10m);
        var exitLink = await ExitLinkAsync(scenario.OrderId);

        await _orderAppService.RegisterReturnEntryAsync(new OrderReturnEntryDto
        {
            OrderId = scenario.OrderId, BranchId = scenario.BranchId, VaultId = scenario.VaultId,
            Lines = new List<OrderReturnEntryLineDto>
            {
                new() { PhysicalExitLinkId = exitLink.Id, Quantity = 1m, Amount = 10m },
            },
        });

        var links = await WithUnitOfWorkAsync(() => _links.GetListAsync(l => l.OrderId == scenario.OrderId));
        var returnLink = links.Single(l => l.Kind == OrderFulfillmentLinkKind.Return);
        returnLink.RemoteLineId.ShouldBe(RemoteLineId);
        returnLink.FulfilledAmount.ShouldBe(10m);
    }

    /// <summary>③ KISMİ iade gerçektir — çıkan 20 gramın yalnız 8'i dönebilir.</summary>
    [Fact]
    public async Task Partial_return_restores_only_what_came_back()
    {
        var scenario = await SeedFulfilledAsync("RPA", grams: 20m);
        var exitLink = await ExitLinkAsync(scenario.OrderId);

        await _orderAppService.RegisterReturnEntryAsync(new OrderReturnEntryDto
        {
            OrderId = scenario.OrderId, BranchId = scenario.BranchId, VaultId = scenario.VaultId,
            Lines = new List<OrderReturnEntryLineDto>
            {
                new() { PhysicalExitLinkId = exitLink.Id, Quantity = 1m, Amount = 8m },
            },
        });

        (await StockAsync(scenario)).NetAmount.ShouldBe(38m);   // 30 + 8
    }

    /// <summary>④ ÇIKMAMIŞ malın iadesi REDDEDİLİR — iade tanımı gereği çıkış sonrasıdır.
    /// <para>Sessizce fiş yazılsaydı, hiç satılmamış mal stoğa eklenirdi.</para></summary>
    [Fact]
    public async Task Return_without_a_physical_exit_is_rejected()
    {
        var scenario = await SeedReservedAsync("RNE", grams: 5m);

        (await Should.ThrowAsync<BusinessException>(
            () => _orderAppService.RegisterReturnEntryAsync(new OrderReturnEntryDto
            {
                OrderId = scenario.OrderId, BranchId = scenario.BranchId, VaultId = scenario.VaultId,
                Lines = new List<OrderReturnEntryLineDto>
                {
                    new() { PhysicalExitLinkId = Guid.NewGuid(), Quantity = 1m, Amount = 5m },
                },
            })))
            .Code.ShouldBe("TradeXpress:OrderReturn:NoPhysicalExit");
    }

    /// <summary>⑤ Bilinmeyen çıkış bağı SESSİZ geçilmez — gerekçe raporlanır.</summary>
    [Fact]
    public async Task Unknown_exit_link_is_reported()
    {
        var scenario = await SeedFulfilledAsync("RUL", grams: 6m);

        var ex = await Should.ThrowAsync<BusinessException>(
            () => _orderAppService.RegisterReturnEntryAsync(new OrderReturnEntryDto
            {
                OrderId = scenario.OrderId, BranchId = scenario.BranchId, VaultId = scenario.VaultId,
                Lines = new List<OrderReturnEntryLineDto>
                {
                    new() { PhysicalExitLinkId = Guid.NewGuid(), Quantity = 1m, Amount = 6m },
                },
            }));

        ex.Code.ShouldBe("TradeXpress:OrderReturn:NothingRegistered");
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    private async Task<OrderFulfillmentLink> ExitLinkAsync(Guid orderId)
    {
        var links = await WithUnitOfWorkAsync(
            () => _links.GetListAsync(l => l.OrderId == orderId && l.Kind == OrderFulfillmentLinkKind.PhysicalExit));
        return links.ShouldHaveSingleItem();
    }

    private async Task<MetalStockRowDto> StockAsync(ReturnScenario scenario)
    {
        var rows = await _metalReport.GetStockAsync(new MetalReportFilterDto());
        return rows.Single(r => r.MetalId == scenario.MetalId);
    }

    private async Task<ReturnScenario> SeedFulfilledAsync(string prefix, decimal grams)
    {
        var scenario = await SeedReservedAsync(prefix, grams);

        await _orderAppService.FulfillReservationAsync(new OrderFulfillmentInputDto
        {
            OrderId = scenario.OrderId, BranchId = scenario.BranchId, VaultId = scenario.VaultId,
        });

        return scenario;
    }

    private async Task<ReturnScenario> SeedReservedAsync(string prefix, decimal grams)
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
                quantity: 0m, amount: grams, factor: 1m, valuationUnitId: data.HasUnitId,
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

            var operational = new OrderLineOperationalData(data.CompanyId, order.Id, RemoteLineId);
            operational.SetProductMatch(variant.Id, $"{prefix} Varyant", null, DateTime.UtcNow);
            await _operationalLines.InsertAsync(operational, autoSave: true);

            return order.Id;
        });

        await WithUnitOfWorkAsync(() => _reservationManager.EnsureReservationAsync(data.CompanyId, orderId));

        return new ReturnScenario(data.CompanyId, orderId, metalId, data.BranchId, data.VaultId);
    }

    private sealed record ReturnScenario(Guid CompanyId, Guid OrderId, Guid MetalId, Guid BranchId, Guid VaultId);
}
