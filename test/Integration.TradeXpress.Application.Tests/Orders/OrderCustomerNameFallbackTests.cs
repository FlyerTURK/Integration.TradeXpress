using System;
using System.Linq;
using System.Reflection;
using Integration.TradeXpress.Orders;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Sipariş listesindeki "Müşteri" kolonunun boş kalmama garantisi (2026-07-28 Hakan): kanal bu alanı boş
/// gönderebiliyor (N11'de sık), o durumda sırayla ALICI ve TESLİMAT alıcısı adına düşülür.
///
/// <para>Neden mekanik ağ: fallback tek bir private yardımcıda yaşıyor ve sessizce kaldırılırsa kolon yeniden
/// boşalır — kimse fark etmez, çünkü dolu müşteri adı olan siparişlerde davranış aynı kalır.</para>
/// </summary>
public class OrderCustomerNameFallbackTests
{
    [Fact]
    public void Uses_the_channel_customer_name_when_present()
    {
        var order = BuildOrder(customerName: "Mevlüt Yaşar", buyerName: "Alıcı Adı", shippingName: "Teslimat Adı");

        Resolve(order).ShouldBe("Mevlüt Yaşar");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Falls_back_to_the_buyer_when_customer_name_is_missing(string? customerName)
    {
        var order = BuildOrder(customerName, buyerName: "Alıcı Adı", shippingName: "Teslimat Adı");

        Resolve(order).ShouldBe("Alıcı Adı");
    }

    [Fact]
    public void Falls_back_to_the_shipping_recipient_when_buyer_is_also_missing()
    {
        var order = BuildOrder(customerName: null, buyerName: null, shippingName: "Teslimat Adı");

        Resolve(order).ShouldBe("Teslimat Adı");
    }

    [Fact]
    public void Returns_nothing_when_no_name_exists_anywhere()
    {
        // Hiçbir ad yoksa uydurulmuş bir değer DÖNMEZ — kolon boş kalır (yanlış ad göstermekten iyidir).
        var order = BuildOrder(customerName: null, buyerName: null, shippingName: null);

        Resolve(order).ShouldBeNullOrWhiteSpace();
    }

    [Fact]
    public void Survives_orders_without_any_detail_snapshot()
    {
        // Detay hiç çekilmemiş sipariş (Detail null) fallback'i patlatmamalı.
        var order = NewOrder(customerName: null);

        Resolve(order).ShouldBeNullOrWhiteSpace();
    }

    /// <summary>Kimlik alanları ctor'da YOK (ABP atar); uzak snapshot ApplyRemote ile yerleşir.</summary>
    private static Order NewOrder(string? customerName)
    {
        var order = new Order(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            SalesChannels.SalesChannelType.TrN11,
            "REMOTE-1",
            "SIP-1");

        order.ApplyRemote(
            orderNumber: "SIP-1",
            orderDate: new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc),
            neutralStatus: OrderStatus.New,
            remoteStatus: null,
            customerName: customerName,
            totalAmount: 0m,
            currencyUnitId: null,
            cargoProvider: null,
            cargoTrackingNumber: null,
            fetchedAt: new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc));

        return order;
    }

    private static Order BuildOrder(string? customerName, string? buyerName, string? shippingName)
    {
        var order = NewOrder(customerName);

        order.SetDetail(new OrderDetailSnapshot(
            buyer: buyerName is null ? null : new OrderDetailParty(buyerName, null, null, null, null),
            billingAddress: null,
            shippingAddress: shippingName is null
                ? null
                : new OrderDetailAddress(shippingName, null, null, null, null, null, null, null, null, null),
            invoiceType: null,
            paymentType: null,
            citizenshipId: null,
            totals: null,
            items: null,
            fetchedAt: new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc)));

        return order;
    }

    /// <summary>Fallback AppService'in private yardımcısında yaşıyor; test onu yansımayla çağırır —
    /// mantığı yalnız test görsün diye public'e açmak (yüzeyi genişletmek) yanlış olurdu.</summary>
    private static string? Resolve(Order order)
    {
        var method = typeof(OrderAppService).GetMethod(
            "ResolveCustomerName",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.ShouldNotBeNull("ResolveCustomerName kaldırılmış ya da yeniden adlandırılmış olabilir.");
        return (string?)method!.Invoke(null, new object[] { order });
    }
}
