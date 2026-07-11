using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Orders;

/// <summary><see cref="TrendyolOrderClient.FetchAllWindowsAsync"/> saf tarih-penceresi döngüsü birim testleri —
/// Trendyol'un 2-hafta-aralık şartına uyacak şekilde geçmişi 14 günlük ardışık pencerelere böler, son pencereyi
/// "şimdi"ye kırpar, sonuçları birleştirir (ağ yok; pencere kaynağı sahte delege).</summary>
public class TrendyolOrderWindowTests
{
    [Fact]
    public async Task Splits_into_14_day_windows_and_clamps_last_to_now()
    {
        var since = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc); // 29 gün → 14 + 14 + 1
        var windows = new List<(long Start, long End)>();

        var result = await TrendyolOrderClient.FetchAllWindowsAsync(since, now, (start, end) =>
        {
            windows.Add((start, end));
            return Task.FromResult(new List<RemoteOrder>
            {
                new(
                    RemoteOrderId: "R" + windows.Count,
                    OrderNumber: "N",
                    OrderDate: since,
                    RemoteStatus: null,
                    CustomerName: null,
                    TotalAmount: 0m,
                    CargoProvider: null,
                    CargoTrackingNumber: null,
                    Lines: new List<RemoteOrderLine>()),
            });
        });

        windows.Count.ShouldBe(3);                       // 14 + 14 + 1 gün
        result.Count.ShouldBe(3);                        // her pencereden 1 sipariş, birleşti
        ToUtc(windows[0].Start).ShouldBe(since);         // ilk pencere since'ten başlar
        ToUtc(windows[2].End).ShouldBe(now);             // son pencere now'a kırpılır (14 günden kısa)
        ToUtc(windows[0].End).ShouldBe(ToUtc(windows[1].Start)); // pencereler ardışık (ayrık, boşluksuz)
    }

    [Fact]
    public async Task No_window_when_since_is_not_before_now()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var calls = 0;

        var result = await TrendyolOrderClient.FetchAllWindowsAsync(t, t, (start, end) =>
        {
            calls++;
            return Task.FromResult(new List<RemoteOrder>());
        });

        calls.ShouldBe(0);
        result.ShouldBeEmpty();
    }

    private static DateTime ToUtc(long epochMs)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime;
    }
}
