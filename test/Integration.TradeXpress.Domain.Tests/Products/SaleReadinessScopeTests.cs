using System;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// <see cref="SaleReadinessScope"/> SAF testleri — kapsam yolu, satışa-hazırlık issue'sunun "hangi ekranda görünür"
/// sözleşmesidir (2026-08-19 Hakan kuralı: bulgu bulunduğu HER seviyede görünür). Her ekran ön-ek kıyası yapar,
/// dolayısıyla SEGMENT SINIRI kuralın kalbidir: <c>variants</c> kapsamı <c>variantsummary</c>'yi kapsasaydı
/// alakasız bir bölüm kırmızıya boyanır ve kullanıcı olmayan bir hatayı arardı.
/// KIRMIZIYSA yol biçimi ya da kıyas semantiği kaymıştır — testi gevşetme.
/// </summary>
public class SaleReadinessScopeTests
{
    private static readonly Guid ChannelProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid VariantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ── Yol kuruluşu ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Variant_paths_nest_under_the_variants_scope()
    {
        SaleReadinessScope.Variant(VariantId).ShouldBe($"variants/{VariantId}");
        SaleReadinessScope.VariantRecipe(VariantId).ShouldBe($"variants/{VariantId}/recipe");
    }

    [Fact]
    public void Channel_paths_nest_channel_then_variant_then_recipe()
    {
        SaleReadinessScope.Channel(ChannelProductId).ShouldBe($"channels/{ChannelProductId}");
        SaleReadinessScope.ChannelVariants(ChannelProductId).ShouldBe($"channels/{ChannelProductId}/variants");
        SaleReadinessScope.ChannelVariant(ChannelProductId, VariantId)
            .ShouldBe($"channels/{ChannelProductId}/variants/{VariantId}");
        SaleReadinessScope.ChannelVariantRecipe(ChannelProductId, VariantId)
            .ShouldBe($"channels/{ChannelProductId}/variants/{VariantId}/recipe");
    }

    // ── Segment sınırı (kuralın kalbi) ────────────────────────────────────────────────────────────────

    /// <summary>Ham <c>StartsWith</c> olsaydı BURASI yeşil kalır ve alakasız bölüm boyanırdı.</summary>
    [Fact]
    public void A_scope_does_not_capture_a_path_that_merely_starts_with_its_letters()
    {
        SaleReadinessScope.IsWithin("variantsummary", SaleReadinessScope.Variants).ShouldBeFalse();
        SaleReadinessScope.IsWithin("channelsettings/x", SaleReadinessScope.Channels).ShouldBeFalse();
    }

    [Fact]
    public void A_scope_captures_itself_and_everything_below_it()
    {
        SaleReadinessScope.IsWithin(SaleReadinessScope.Variants, SaleReadinessScope.Variants).ShouldBeTrue();
        SaleReadinessScope.IsWithin(SaleReadinessScope.Variant(VariantId), SaleReadinessScope.Variants).ShouldBeTrue();
        SaleReadinessScope.IsWithin(SaleReadinessScope.VariantRecipe(VariantId), SaleReadinessScope.Variants).ShouldBeTrue();
        SaleReadinessScope.IsWithin(SaleReadinessScope.VariantRecipe(VariantId), SaleReadinessScope.Variant(VariantId)).ShouldBeTrue();
    }

    /// <summary>Derin kanal issue'su ZİNCİRİN TAMAMINDA görünür: kanal sekmesi → kanal ürünü → varyant sekmesi →
    /// varyant satırı → reçete bölümü. Hakan senaryosunun tek issue ile beş ekranı işaretlemesi buna dayanır.</summary>
    [Fact]
    public void A_deep_channel_variant_recipe_path_is_within_every_level_above_it()
    {
        var path = SaleReadinessScope.ChannelVariantRecipe(ChannelProductId, VariantId);

        SaleReadinessScope.IsWithin(path, SaleReadinessScope.Channels).ShouldBeTrue();
        SaleReadinessScope.IsWithin(path, SaleReadinessScope.Channel(ChannelProductId)).ShouldBeTrue();
        SaleReadinessScope.IsWithin(path, SaleReadinessScope.ChannelVariants(ChannelProductId)).ShouldBeTrue();
        SaleReadinessScope.IsWithin(path, SaleReadinessScope.ChannelVariant(ChannelProductId, VariantId)).ShouldBeTrue();
        SaleReadinessScope.IsWithin(path, path).ShouldBeTrue();
    }

    /// <summary>Kanal yolu ile CORE (ürünün kendi) varyant yolu ayrı ağaçlardır: kanal issue'su ürün formunun varyant
    /// sekmesini boyamaz (orada düzeltilecek bir şey yoktur), tersi de geçerlidir.</summary>
    [Fact]
    public void Channel_and_core_variant_trees_do_not_capture_each_other()
    {
        var channelPath = SaleReadinessScope.ChannelVariantRecipe(ChannelProductId, VariantId);
        var corePath = SaleReadinessScope.VariantRecipe(VariantId);

        SaleReadinessScope.IsWithin(channelPath, SaleReadinessScope.Variants).ShouldBeFalse();
        SaleReadinessScope.IsWithin(corePath, SaleReadinessScope.Channels).ShouldBeFalse();
    }

    [Fact]
    public void A_sibling_channel_product_scope_captures_nothing_of_the_other()
    {
        var otherChannelProductId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        SaleReadinessScope.IsWithin(
            SaleReadinessScope.ChannelVariantRecipe(ChannelProductId, VariantId),
            SaleReadinessScope.Channel(otherChannelProductId)).ShouldBeFalse();
    }

    // ── Kenar durumlar ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Boş kapsam = KÖK: "hepsi" sorusunu soran ekran (satışa hazırlık panelinin issue listesi) her issue'yu görür.</summary>
    [Fact]
    public void An_empty_scope_is_the_root_and_captures_every_path()
    {
        SaleReadinessScope.IsWithin(SaleReadinessScope.General, null).ShouldBeTrue();
        SaleReadinessScope.IsWithin(SaleReadinessScope.General, string.Empty).ShouldBeTrue();
        SaleReadinessScope.IsWithin(null, null).ShouldBeTrue();
    }

    /// <summary>Yolsuz issue hiçbir DAR kapsama düşmez — sessizce kaybolmasın diye sunucu yolu ZORUNLU doldurur.</summary>
    [Fact]
    public void A_pathless_issue_falls_into_no_narrow_scope()
    {
        SaleReadinessScope.IsWithin(null, SaleReadinessScope.Variants).ShouldBeFalse();
        SaleReadinessScope.IsWithin(string.Empty, SaleReadinessScope.Variants).ShouldBeFalse();
    }

    /// <summary>Kapsamlar birbirinin ön-eki değildir: genel/medya/doğrulama kolları ayrık kalır.</summary>
    [Fact]
    public void Top_level_scopes_are_disjoint()
    {
        SaleReadinessScope.IsWithin(SaleReadinessScope.General, SaleReadinessScope.Variants).ShouldBeFalse();
        SaleReadinessScope.IsWithin(SaleReadinessScope.Media, SaleReadinessScope.General).ShouldBeFalse();
        SaleReadinessScope.IsWithin(SaleReadinessScope.Verification, SaleReadinessScope.Variants).ShouldBeFalse();
    }
}
