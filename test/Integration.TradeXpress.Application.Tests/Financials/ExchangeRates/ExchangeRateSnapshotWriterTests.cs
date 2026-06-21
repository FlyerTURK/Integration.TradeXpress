using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.ExchangeRates;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.Financials.ExchangeRates;

public abstract class ExchangeRateSnapshotWriterTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ExchangeRateSnapshotWriter _writer;
    private readonly IRepository<ExchangeRate, Guid> _rateRepo;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepo;

    protected ExchangeRateSnapshotWriterTests()
    {
        _writer = GetRequiredService<ExchangeRateSnapshotWriter>();
        _rateRepo = GetRequiredService<IRepository<ExchangeRate, Guid>>();
        _unitRepo = GetRequiredService<IRepository<CurrencyUnit, Guid>>();
    }

    private static MarketQuote Quote(string code, decimal buy, decimal sell) =>
        new() { Code = code, Buy = buy, Sell = sell, Source = "Harem" };

    [Fact]
    public async Task Writes_a_host_raw_market_row()
    {
        var window = new DateTime(2026, 6, 12, 10, 0, 0, DateTimeKind.Utc);

        await WithUnitOfWorkAsync(async () =>
        {
            var n = await _writer.WriteAsync(window, new[] { Quote(CurrencyUnitCode.USD, 40m, 41m) });
            n.ShouldBe(1);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var usd = await _unitRepo.FirstOrDefaultAsync(u => u.TenantId == null && u.Code == CurrencyUnitCode.USD);
            var row = await _rateRepo.FirstOrDefaultAsync(r => r.CurrencyUnitId == usd!.Id && r.RateDate == window);
            row.ShouldNotBeNull();
            row!.MarketPriceOnBuy.ShouldBe(40m);
            row.MarketPriceOnSell.ShouldBe(41m);
            row.TenantId.ShouldBeNull(); // host
        });
    }

    [Fact]
    public async Task Same_window_is_idempotent()
    {
        var window = new DateTime(2026, 6, 12, 10, 15, 0, DateTimeKind.Utc);

        await WithUnitOfWorkAsync(async () =>
            (await _writer.WriteAsync(window, new[] { Quote(CurrencyUnitCode.EUR, 47m, 48m) })).ShouldBe(1));

        await WithUnitOfWorkAsync(async () =>
            (await _writer.WriteAsync(window, new[] { Quote(CurrencyUnitCode.EUR, 47m, 48m) })).ShouldBe(0));
    }

    [Fact]
    public async Task Unchanged_price_in_next_window_is_skipped()
    {
        var w1 = new DateTime(2026, 6, 12, 11, 0, 0, DateTimeKind.Utc);
        var w2 = new DateTime(2026, 6, 12, 11, 15, 0, DateTimeKind.Utc);

        await WithUnitOfWorkAsync(async () =>
            (await _writer.WriteAsync(w1, new[] { Quote(CurrencyUnitCode.GBP, 55m, 56m) })).ShouldBe(1));

        // Aynı fiyat, sonraki pencere → değişmedi → atla.
        await WithUnitOfWorkAsync(async () =>
            (await _writer.WriteAsync(w2, new[] { Quote(CurrencyUnitCode.GBP, 55m, 56m) })).ShouldBe(0));
    }

    [Fact]
    public async Task Inverted_feed_is_guarded_and_flagged()
    {
        var window = new DateTime(2026, 6, 12, 12, 0, 0, DateTimeKind.Utc);

        await WithUnitOfWorkAsync(async () =>
            (await _writer.WriteAsync(window, new[] { Quote(CurrencyUnitCode.CHF, 60m, 58m) })).ShouldBe(1));

        await WithUnitOfWorkAsync(async () =>
        {
            var chf = await _unitRepo.FirstOrDefaultAsync(u => u.TenantId == null && u.Code == CurrencyUnitCode.CHF);
            var row = await _rateRepo.FirstOrDefaultAsync(r => r.CurrencyUnitId == chf!.Id && r.RateDate == window);
            row.ShouldNotBeNull();
            row!.MarketPriceOnBuy.ShouldBe(58m);  // swap
            row.MarketPriceOnSell.ShouldBe(60m);
            row.GuardFired.ShouldBeTrue();
        });
    }
}
