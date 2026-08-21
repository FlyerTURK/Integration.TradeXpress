using Shouldly;
using Xunit;

namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>
/// <see cref="PackageDesiResolver"/> testleri. Desi kargo tarifesinin girdisidir ve tarife doğrudan satış
/// fiyatına girer — bu yüzden "hangi değer kazanır" kuralı tek yerde ve testle sabitlenir.
/// </summary>
public class PackageDesiResolverTests
{
    [Fact]
    public void Variant_value_wins_over_channel_default()
    {
        PackageDesiResolver.Resolve(variantPackageDesi: 5, channelDefaultPackageDesi: 1).ShouldBe(5);
    }

    [Fact]
    public void Channel_default_is_used_when_variant_has_no_value()
    {
        PackageDesiResolver.Resolve(variantPackageDesi: null, channelDefaultPackageDesi: 3).ShouldBe(3);
    }

    /// <summary>0 BOŞ DEĞİLDİR — pazaryerinin "Dosya" basamağıdır ve geçerli bir override'dır.
    /// Kural "?? " ile yazıldığında da doğru çalışır ama burada niyet sabitleniyor: varyantta 0 yazan kullanıcı
    /// kanal varsayılanına düşmemeli.</summary>
    [Fact]
    public void Variant_zero_is_a_real_override_not_an_empty_value()
    {
        PackageDesiResolver.Resolve(variantPackageDesi: 0, channelDefaultPackageDesi: 4).ShouldBe(0);
    }

    /// <summary>Negatif desi diye bir şey yok; entity'ler zaten fail-fast atar ama çözümleyici de savunmacı
    /// davranır (bozuk eski veri sessizce negatif fiyat üretmesin).</summary>
    [Theory]
    [InlineData(-1, 2, 0)]
    [InlineData(null, -5, 0)]
    public void Negative_values_are_clamped_to_zero(int? variantDesi, int channelDefault, int expected)
    {
        PackageDesiResolver.Resolve(variantDesi, channelDefault).ShouldBe(expected);
    }
}
