using Integration.TradeXpress.SalesChannels;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// <see cref="ChannelSequenceCode"/> mekanik ağı — "-1" YASAĞININ çivisi (2026-08-07 Hakan bulgusu).
///
/// <para>Altı kanal üreticisi (SellerCode · ProductMainId · SellerSkuBase + üç varyant SKU kodu) bu tek kurala
/// bağlı: ilk sahip çıplak kodu alır, son ek 2'den başlar. Kural burada gevşetilirse (ör. birisi "-1"i geri
/// getirirse) altı üretici birden bozulur — bu test onu kırmızıya çevirir.</para>
/// </summary>
public class ChannelSequenceCodeTests
{
    [Fact]
    public void First_sequence_returns_the_bare_code()
    {
        ChannelSequenceCode.Compose("1234", 1).ShouldBe("1234");
    }

    [Fact]
    public void Suffix_starts_at_two()
    {
        ChannelSequenceCode.Compose("1234", 2).ShouldBe("1234-2");
        ChannelSequenceCode.Compose("1234", 3).ShouldBe("1234-3");
    }

    [Fact]
    public void No_producer_ever_emits_a_minus_one_suffix()
    {
        // Sıfır/negatif sıra bozuk veridir; çıplak koda düşer — "-0"/"-1" ASLA üretilmez.
        ChannelSequenceCode.Compose("1234", 0).ShouldBe("1234");
        ChannelSequenceCode.Compose("1234", -1).ShouldBe("1234");
    }
}
