using System;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// <see cref="ProcessDirectionType"/> sayısal değer KİLİDİ. Kod tabanı yaygın olarak
/// <c>(int)Direction % 2 == 0</c> = giriş (inflow) konvansiyonuna dayanır
/// (VoucherLineCalculator, BullionLegCalculator, tüm poster'lar). Enum yeniden
/// sıralanır/araya üye girerse TÜM bakiye işaretleri ters döner — bu testler o
/// durumda KIRMIZI yanmalıdır; test asla enum'a uydurulmaz, enum düzeltilir.
/// </summary>
public class ProcessDirectionTypeTests
{
    [Theory]
    [InlineData(ProcessDirectionType.Inbound,  0)]
    [InlineData(ProcessDirectionType.Outbound, 1)]
    [InlineData(ProcessDirectionType.Credit,   2)]
    [InlineData(ProcessDirectionType.Debit,    3)]
    [InlineData(ProcessDirectionType.Buy,      4)]
    [InlineData(ProcessDirectionType.Sell,     5)]
    public void Numeric_values_are_locked(ProcessDirectionType member, int expected)
    {
        ((int)member).ShouldBe(expected);
    }

    [Theory]
    [InlineData(ProcessDirectionType.Inbound,  true)]    // çift → giriş (+)
    [InlineData(ProcessDirectionType.Outbound, false)]   // tek  → çıkış (−)
    [InlineData(ProcessDirectionType.Credit,   true)]
    [InlineData(ProcessDirectionType.Debit,    false)]
    [InlineData(ProcessDirectionType.Buy,      true)]
    [InlineData(ProcessDirectionType.Sell,     false)]
    public void Even_members_are_inflow_odd_members_are_outflow(ProcessDirectionType member, bool expectedInflow)
    {
        (((int)member % 2) == 0).ShouldBe(expectedInflow);
    }

    [Theory]
    [InlineData(ProcessDirectionType.Inbound,  true)]    // çift → giriş (+)
    [InlineData(ProcessDirectionType.Outbound, false)]   // tek  → çıkış (−)
    [InlineData(ProcessDirectionType.Credit,   true)]
    [InlineData(ProcessDirectionType.Debit,    false)]
    [InlineData(ProcessDirectionType.Buy,      true)]
    [InlineData(ProcessDirectionType.Sell,     false)]
    public void IsInflow_extension_matches_even_odd_convention(ProcessDirectionType member, bool expectedInflow)
    {
        // Konvansiyonun TEK kaynağı ProcessDirectionTypeExtensions — ham %2 ile birebir aynı olmalı.
        member.IsInflow().ShouldBe(expectedInflow);
        member.IsOutflow().ShouldBe(!expectedInflow);
    }

    [Fact]
    public void Member_set_is_locked()
    {
        // Yeni üye eklemek başlı başına yasak değil — ama %2 konvansiyonuna uygun
        // konumlandırılmalı ve bu kilit BİLİNÇLİ güncellenmelidir.
        Enum.GetValues<ProcessDirectionType>().ShouldBe(new[]
        {
            ProcessDirectionType.Inbound,
            ProcessDirectionType.Outbound,
            ProcessDirectionType.Credit,
            ProcessDirectionType.Debit,
            ProcessDirectionType.Buy,
            ProcessDirectionType.Sell,
        });
    }

    [Fact]
    public void Underlying_type_is_byte()
    {
        Enum.GetUnderlyingType(typeof(ProcessDirectionType)).ShouldBe(typeof(byte));
    }
}
