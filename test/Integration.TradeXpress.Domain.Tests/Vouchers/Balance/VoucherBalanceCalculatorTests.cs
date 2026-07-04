using System.Collections.Generic;
using Shouldly;
using Xunit;
using static Integration.TradeXpress.Vouchers.Balance.BalanceTestLine;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// <see cref="VoucherBalanceCalculator"/> testleri — poster yönlendirme + birim bazında
/// net toplama (<c>Aggregate</c>). DI yerine poster'lar elle verilir (saf test).
/// </summary>
public class VoucherBalanceCalculatorTests
{
    private static VoucherBalanceCalculator Calculator(params IVoucherLineBalancePoster[] posters)
    {
        return new VoucherBalanceCalculator(posters);
    }

    [Fact]
    public void Post_routes_line_to_matching_process_type_poster()
    {
        var calculator = Calculator(new CashBalancePoster(), new ConvertBalancePoster());
        var line = Create(ProcessType.Cash, payUnitId: TryUnit, payTotal: 100m);

        calculator.Post(line).ShouldBe(new[] { new BalanceEffect(TryUnit, 100m) });
    }

    [Fact]
    public void Post_without_registered_poster_returns_empty()
    {
        // Poster'ı olmayan tür bakiyeyi etkilemez — sessizce atlanır.
        var calculator = Calculator(new CashBalancePoster());
        var line = Create(ProcessType.Stone, payUnitId: TryUnit, payTotal: 100m);

        calculator.Post(line).ShouldBeEmpty();
    }

    [Fact]
    public void Aggregate_nets_effects_per_unit_across_lines_and_posters()
    {
        var calculator = Calculator(new CashBalancePoster(), new ConvertBalancePoster());
        var lines = new[]
        {
            // Nakit: +100 TRY ve −30 TRY.
            Create(ProcessType.Cash, ProcessDirectionType.Inbound,  payUnitId: TryUnit, payTotal: 100m),
            Create(ProcessType.Cash, ProcessDirectionType.Outbound, payUnitId: TryUnit, payTotal: 30m),
            // Çevir (Alacak): −100 USD, +4500 TRY.
            Create(ProcessType.Convert, ProcessDirectionType.Credit,
                   mainUnitId: UsdUnit, total: 100m, payUnitId: TryUnit, payTotal: 4500m),
        };

        var net = calculator.Aggregate(lines);

        net.Count.ShouldBe(2);
        net[TryUnit].ShouldBe(4570m);   // 100 − 30 + 4500
        net[UsdUnit].ShouldBe(-100m);
    }

    [Fact]
    public void Aggregate_transfer_twin_lines_net_to_zero()
    {
        // Virman ikizleri: kaynak (Çıkış −) + karşı (Giriş +) → toplam etki sıfır.
        var calculator = Calculator(new TransferBalancePoster());
        var lines = new[]
        {
            Create(ProcessType.Transfer, ProcessDirectionType.Outbound, payUnitId: TryUnit, payTotal: 250m),
            Create(ProcessType.Transfer, ProcessDirectionType.Inbound,  payUnitId: TryUnit, payTotal: 250m),
        };

        var net = calculator.Aggregate(lines);

        net[TryUnit].ShouldBe(0m);
    }

    [Fact]
    public void Aggregate_skips_lines_without_poster_but_keeps_the_rest()
    {
        var calculator = Calculator(new CashBalancePoster());
        var lines = new[]
        {
            Create(ProcessType.Cash,  payUnitId: TryUnit, payTotal: 100m),
            Create(ProcessType.Stone, payUnitId: TryUnit, payTotal: 999m),   // poster yok → atlanır
        };

        var net = calculator.Aggregate(lines);

        net.Count.ShouldBe(1);
        net[TryUnit].ShouldBe(100m);
    }

    [Fact]
    public void Duplicate_posters_for_same_process_type_first_wins()
    {
        // Tasarım sözleşmesi: aynı ProcessType'a iki poster kaydolursa İLKİ kazanır.
        var calculator = Calculator(new StubCashPoster(111m), new StubCashPoster(222m));
        var line = Create(ProcessType.Cash, payUnitId: TryUnit, payTotal: 0m);

        calculator.Post(line).ShouldBe(new[] { new BalanceEffect(TryUnit, 111m) });
    }

    /// <summary>Ayırt edilebilir sabit etki üreten test poster'ı (ilki-kazanır sözleşmesi için).</summary>
    private sealed class StubCashPoster : IVoucherLineBalancePoster
    {
        private readonly decimal _marker;

        public StubCashPoster(decimal marker)
        {
            _marker = marker;
        }

        public ProcessType ProcessType
        {
            get { return ProcessType.Cash; }
        }

        public IEnumerable<BalanceEffect> Post(VoucherLine line)
        {
            yield return new BalanceEffect(TryUnit, _marker);
        }
    }
}
