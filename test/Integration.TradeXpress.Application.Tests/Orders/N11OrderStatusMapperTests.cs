using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary><see cref="N11OrderStatusMapper"/> birim testleri — N11 order.status kodu (SOAP ref v4.6 GROUND TRUTH) →
/// nötr <see cref="OrderStatus"/>. Bilinmeyen/geçersiz (4) → Unknown (sessizce "New" varsayılmaz; ham RemoteStatus korunur).</summary>
public class N11OrderStatusMapperTests
{
    [Theory]
    [InlineData("1", OrderStatus.New)]         // İşlem Bekliyor
    [InlineData("2", OrderStatus.Processing)]  // İşlemde
    [InlineData("3", OrderStatus.Cancelled)]   // İptal Edilmiş
    [InlineData("5", OrderStatus.Delivered)]   // Tamamlandı
    public void Maps_known_order_status_codes(string remote, OrderStatus expected)
    {
        N11OrderStatusMapper.Map(remote).ShouldBe(expected);
    }

    [Theory]
    [InlineData("4")]      // Geçersiz — belirsiz, nötr duruma zorlanmaz
    [InlineData("99")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    public void Unknown_invalid_or_empty_maps_to_Unknown(string? remote)
    {
        N11OrderStatusMapper.Map(remote).ShouldBe(OrderStatus.Unknown);
    }
}
