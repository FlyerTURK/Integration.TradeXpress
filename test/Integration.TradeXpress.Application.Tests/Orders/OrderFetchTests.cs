using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Sipariş ÇEKİM testleri (Sipariş Fazı O0) — sahte client'la (ağ yok, READ-ONLY) uçtan uca: ilk çekim NÖTR Order +
/// satırları üretir; ikinci çekim İDEMPOTENT'tir (0 yeni + durum/satır güncelleme, dublike YOK); ortak liste
/// kanal/durum filtresiyle çalışır; nötr status eşleme uygulanır; OrderLine SNAPSHOT olarak yerel ürün silinse bile
/// SAĞ KALIR (id-only ProductVariantId, sert FK/cascade yok). FİŞ/REZERVASYON/STOK'a HİÇ dokunulmaz.
/// </summary>
public abstract class OrderFetchTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IOrderAppService _appService;
    private readonly FakeTrendyolOrderClient _fakeClient;
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _channelRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<OrderLine, Guid> _lineRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<ProductVariant, Guid> _variantRepository;
    private readonly ICurrentCompany _currentCompany;

    protected OrderFetchTests()
    {
        _appService = GetRequiredService<IOrderAppService>();
        _fakeClient = GetRequiredService<FakeTrendyolOrderClient>();
        _channelRepository = GetRequiredService<IRepository<SalesChannelTrTrendyol, Guid>>();
        _orderRepository = GetRequiredService<IRepository<Order, Guid>>();
        _lineRepository = GetRequiredService<IRepository<OrderLine, Guid>>();
        _productRepository = GetRequiredService<IRepository<Product, Guid>>();
        _variantRepository = GetRequiredService<IRepository<ProductVariant, Guid>>();
        _currentCompany = GetRequiredService<ICurrentCompany>();
    }

    [Fact]
    public async Task First_fetch_creates_orders_and_lines_with_neutral_status()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "ORD1");
            _fakeClient.RemoteOrders.Clear();
            _fakeClient.RemoteOrders.Add(BuildOrder("R-1", "TY-1", "Shipped", 300m,
                ("BR-1", "Ürün Bir", 2m, 100m), ("BR-2", "Ürün İki", 1m, 100m)));
            _fakeClient.RemoteOrders.Add(BuildOrder("R-2", "TY-2", "Delivered", 50m, ("BR-3", "Ürün Üç", 1m, 50m)));

            var report = await _appService.FetchOrdersAsync(channel.Id);

            report.FetchedOrders.ShouldBe(2);
            report.NewOrders.ShouldBe(2);
            report.UpdatedOrders.ShouldBe(0);
            report.TotalLines.ShouldBe(3);

            var orders = await WithUnitOfWorkAsync(async () =>
                await _orderRepository.GetListAsync(o => o.CompanyId == companyId));
            orders.Count.ShouldBe(2);
            var o1 = orders.Single(o => o.RemoteOrderId == "R-1");
            o1.OrderNumber.ShouldBe("TY-1");
            o1.NeutralStatus.ShouldBe(OrderStatus.Shipped);   // nötr eşleme
            o1.RemoteStatus.ShouldBe("Shipped");
            o1.ChannelType.ShouldBe(SalesChannelType.TrTrendyol);
            o1.TotalAmount.ShouldBe(300m);
            orders.Single(o => o.RemoteOrderId == "R-2").NeutralStatus.ShouldBe(OrderStatus.Delivered);

            var lines = await WithUnitOfWorkAsync(async () =>
                await _lineRepository.GetListAsync(l => l.OrderId == o1.Id));
            lines.Count.ShouldBe(2);
            lines.All(l => l.ProductVariantId == null).ShouldBeTrue();   // O1 rezerve — snapshot esas, link yok
            lines.Single(l => l.Barcode == "BR-1").ProductNameSnapshot.ShouldBe("Ürün Bir");
            lines.Single(l => l.Barcode == "BR-1").LineTotal.ShouldBe(200m);
        }
    }

    [Fact]
    public async Task Second_fetch_is_idempotent_updates_status_without_duplicating()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "ORD2");
            _fakeClient.RemoteOrders.Clear();
            _fakeClient.RemoteOrders.Add(BuildOrder("R-9", "TY-9", "Created", 100m, ("BR-9", "Ürün", 1m, 100m)));

            var first = await _appService.FetchOrdersAsync(channel.Id);
            first.NewOrders.ShouldBe(1);

            // Aynı sipariş yeni durum + değişen satırla tekrar gelir.
            _fakeClient.RemoteOrders.Clear();
            _fakeClient.RemoteOrders.Add(BuildOrder("R-9", "TY-9", "Delivered", 120m, ("BR-9", "Ürün", 1m, 120m)));

            var second = await _appService.FetchOrdersAsync(channel.Id);

            second.FetchedOrders.ShouldBe(1);
            second.NewOrders.ShouldBe(0);
            second.UpdatedOrders.ShouldBe(1);

            var orders = await WithUnitOfWorkAsync(async () =>
                await _orderRepository.GetListAsync(o => o.CompanyId == companyId));
            orders.ShouldHaveSingleItem();                    // dublike YOK
            orders[0].NeutralStatus.ShouldBe(OrderStatus.Delivered);
            orders[0].TotalAmount.ShouldBe(120m);

            var lines = await WithUnitOfWorkAsync(async () =>
                await _lineRepository.GetListAsync(l => l.OrderId == orders[0].Id));
            lines.ShouldHaveSingleItem();                     // satırlar sil+yaz → dublike YOK
            lines[0].UnitPrice.ShouldBe(120m);
        }
    }

    [Fact]
    public async Task Combined_list_filters_by_status_and_enriches_channel_code()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "ORD3");
            _fakeClient.RemoteOrders.Clear();
            _fakeClient.RemoteOrders.Add(BuildOrder("R-A", "TY-A", "Shipped", 10m, ("BR-A", "A", 1m, 10m)));
            _fakeClient.RemoteOrders.Add(BuildOrder("R-B", "TY-B", "Delivered", 20m, ("BR-B", "B", 1m, 20m)));
            await _appService.FetchOrdersAsync(channel.Id);

            var all = await _appService.GetListAsync(new OrderListRequestDto());
            all.TotalCount.ShouldBe(2);
            all.Items.ShouldAllBe(i => i.SalesChannelCode == channel.Code);   // enrich (id-only referanstan)
            all.Items.ShouldAllBe(i => i.ChannelType == SalesChannelType.TrTrendyol);

            var shipped = await _appService.GetListAsync(new OrderListRequestDto
            {
                Filters = new List<FilterField>
                {
                    new() { Field = "NeutralStatus", Operator = ListFilterOperator.Equals, Value = "Shipped" },
                },
            });
            shipped.TotalCount.ShouldBe(1);
            shipped.Items.ShouldHaveSingleItem().OrderNumber.ShouldBe("TY-A");
        }
    }

    // ── Snapshot felsefesi: OrderLine yerel ürün silinse bile SAĞ KALIR (id-only, sert FK/cascade yok) ──

    [Fact]
    public async Task OrderLine_with_variant_link_survives_product_deletion()
    {
        var companyId = Guid.NewGuid();
        using (_currentCompany.Change(companyId))
        {
            var channel = await SeedChannelAsync(companyId, "ORD4");

            // Yerel ürün + varyant (O1'in dolduracağı türden bir bağ) — sonra SİLİNİR.
            var variant = await WithUnitOfWorkAsync(async () =>
            {
                var product = await _productRepository.InsertAsync(new Product(companyId, "PRD-1", "Ürün Bir"), autoSave: true);
                return await _variantRepository.InsertAsync(new ProductVariant(companyId, product.Id, "VAR-1", "Varyant"), autoSave: true);
            });

            // Sipariş + varyanta BAĞLI bir satır (id-only ProductVariantId dolu) elle kurulur.
            var order = await WithUnitOfWorkAsync(async () =>
                await _orderRepository.InsertAsync(
                    new Order(companyId, channel.Id, SalesChannelType.TrTrendyol, "R-LINK", "TY-LINK"), autoSave: true));
            await WithUnitOfWorkAsync(async () =>
            {
                var snapshot = new OrderLineSnapshot("L-1", "BR-L", "STK-L", "Bağlı Satır", 1m, 5m, 5m, "Shipped", variant.Id);
                return await _lineRepository.InsertAsync(new OrderLine(companyId, order.Id, snapshot), autoSave: true);
            });

            // Yerel varyant + ürün SİLİNİR (id-only bağ → cascade YOK; OrderLine bozulmamalı).
            await WithUnitOfWorkAsync(async () =>
            {
                await _variantRepository.DeleteAsync(variant.Id, autoSave: true);
                return true;
            });

            // OrderLine hâlâ TAM ve okunabilir (snapshot kendi gerçeğini taşır; bağ artık "yetim" ama satır sağlam).
            var lines = await WithUnitOfWorkAsync(async () =>
                await _lineRepository.GetListAsync(l => l.OrderId == order.Id));
            var line = lines.ShouldHaveSingleItem();
            line.ProductNameSnapshot.ShouldBe("Bağlı Satır");
            line.ProductVariantId.ShouldBe(variant.Id);        // bağ korunur; hedef silinmiş olsa da satır bozulmaz

            var dto = await _appService.GetAsync(order.Id);
            dto.Lines.ShouldHaveSingleItem().ProductNameSnapshot.ShouldBe("Bağlı Satır");
        }
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────────────────────────

    private async Task<SalesChannelTrTrendyol> SeedChannelAsync(Guid companyId, string suffix)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _channelRepository.InsertAsync(
                new SalesChannelTrTrendyol(companyId, $"TY-{suffix}", $"Trendyol {suffix}", "seller-1", "api-key", "api-secret"),
                autoSave: true));
    }

    private static RemoteOrder BuildOrder(
        string remoteId, string orderNumber, string status, decimal amount, params (string Barcode, string Name, decimal Qty, decimal Price)[] lines)
    {
        return new RemoteOrder(
            RemoteOrderId: remoteId,
            OrderNumber: orderNumber,
            OrderDate: DateTime.UtcNow,
            RemoteStatus: status,
            CustomerName: "Test Müşteri",
            TotalAmount: amount,
            CargoProvider: "Test Kargo",
            CargoTrackingNumber: "12345",
            Lines: lines.Select(l => new RemoteOrderLine(
                RemoteLineId: null,
                Barcode: l.Barcode,
                StockCode: null,
                ProductName: l.Name,
                Quantity: l.Qty,
                UnitPrice: l.Price,
                LineTotal: l.Qty * l.Price,
                RemoteLineStatus: status)).ToList());
    }
}
