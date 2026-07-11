using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary><see cref="TrendyolOrderStatusMapper"/> birim testleri — ham Trendyol durumu → nötr <see cref="OrderStatus"/>
/// (case-insensitive; bilinmeyen/boş → Unknown, sessizce "New" varsayılmaz).</summary>
public class TrendyolOrderStatusMapperTests
{
    [Theory]
    [InlineData("Created", OrderStatus.New)]
    [InlineData("Picking", OrderStatus.Processing)]
    [InlineData("Invoiced", OrderStatus.Processing)]
    [InlineData("SHIPPED", OrderStatus.Shipped)]
    [InlineData("AtCollectionPoint", OrderStatus.Shipped)]
    [InlineData("Delivered", OrderStatus.Delivered)]
    [InlineData("Cancelled", OrderStatus.Cancelled)]
    [InlineData("UnSupplied", OrderStatus.Cancelled)]
    [InlineData("Returned", OrderStatus.Returned)]
    [InlineData("UnDelivered", OrderStatus.Returned)]
    public void Maps_known_statuses(string remote, OrderStatus expected)
    {
        TrendyolOrderStatusMapper.Map(remote).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("SomethingNew")]
    public void Unknown_or_empty_maps_to_Unknown_not_New(string? remote)
    {
        TrendyolOrderStatusMapper.Map(remote).ShouldBe(OrderStatus.Unknown);
    }
}
