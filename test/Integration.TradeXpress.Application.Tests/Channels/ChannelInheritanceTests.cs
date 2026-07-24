using System;
using System.Collections.Generic;
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.Products;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.Channels;

/// <summary>K10/K11 devralma zincirinin KİLİDİ (saf sınıf, DI'sız test) — kullanıcı-onaylı kural:
/// kanal-değeri doluysa kanal, değilse ürün. Push (Faz-2) yazılmadan ÖNCE davranış burada sabitlenir;
/// kural değişikliği ancak bu testlerle birlikte yapılabilir (sessiz gevşetme kırmızı yakar).</summary>
public class ChannelInheritanceTests
{
    // ── Skaler / metin / liste zincirleri (diğer alanlarla aynı desen) ──────────────────────────────

    [Fact]
    public void Scalar_chain_prefers_filled_channel_value()
    {
        ChannelInheritance.Resolve<int>(5, 3).ShouldBe(5);
        ChannelInheritance.Resolve<int>(null, 3).ShouldBe(3);
        ChannelInheritance.Resolve<int>(null, null).ShouldBeNull();
    }

    [Fact]
    public void Text_chain_treats_whitespace_as_empty()
    {
        ChannelInheritance.Resolve("kanal", "ürün").ShouldBe("kanal");
        ChannelInheritance.Resolve("   ", "ürün").ShouldBe("ürün");
        ChannelInheritance.Resolve(null, "ürün").ShouldBe("ürün");
        ChannelInheritance.Resolve(null, null).ShouldBeNull();
    }

    [Fact]
    public void List_chain_prefers_non_empty_channel_list()
    {
        var channel = new List<string> { "a" };
        var product = new List<string> { "b", "c" };

        ChannelInheritance.ResolveList(channel, product).ShouldBe(channel);
        ChannelInheritance.ResolveList(new List<string>(), product).ShouldBe(product);
        ChannelInheritance.ResolveList<string>(null, null).ShouldBeEmpty();
    }

    // ── K10: kişiselleştirme bloğu ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Personalization_falls_back_to_product_when_channel_block_is_empty()
    {
        var product = new PersonalizationValues(true, "Ürün talimatı", true, 40);

        // Kanal kişiselleştirme taşımıyor (null) ya da bloğu boş (IsPersonalizable=false) → ürün bloğu TAMAMEN.
        ChannelInheritance.ResolvePersonalization(null, product).ShouldBe(product);
        ChannelInheritance.ResolvePersonalization(
            new PersonalizationValues(false, null, false, null), product).ShouldBe(product);
    }

    [Fact]
    public void Personalization_uses_channel_block_when_filled()
    {
        var channel = new PersonalizationValues(true, "Kanal talimatı", false, 20);
        var product = new PersonalizationValues(true, "Ürün talimatı", true, 40);

        var effective = ChannelInheritance.ResolvePersonalization(channel, product);

        effective.IsPersonalizable.ShouldBeTrue();
        effective.Instructions.ShouldBe("Kanal talimatı");
        effective.IsRequired.ShouldBeFalse();   // kanal bloğu doluyken kanal beyanı esas
        effective.CharCountMax.ShouldBe(20);
    }

    [Fact]
    public void Personalization_nullable_subfields_fall_through_per_field()
    {
        // Kanal bloğu AÇIK ama talimat/karakter sınırı girilmemiş → o alanlar ürüne düşer (alan-bazı desen).
        var channel = new PersonalizationValues(true, null, true, null);
        var product = new PersonalizationValues(false, "Ürün talimatı", false, 64);

        var effective = ChannelInheritance.ResolvePersonalization(channel, product);

        effective.IsPersonalizable.ShouldBeTrue();
        effective.Instructions.ShouldBe("Ürün talimatı");
        effective.IsRequired.ShouldBeTrue();
        effective.CharCountMax.ShouldBe(64);
    }

    [Fact]
    public void Personalization_product_snapshot_reads_product_fields()
    {
        var product = new Product(SimpleGuidGenerator.Instance.Create(), "PRD 01", "Test Ürünü");
        product.SetPersonalization(true, "Kazıma metnini yazın", true, 32);

        PersonalizationValues.Of(product).ShouldBe(
            new PersonalizationValues(true, "Kazıma metnini yazın", true, 32));
    }

    // ── K11: add-on zinciri (bugün tek kaynak ürün ataması; satır-override ?? katalog) ─────────────

    [Fact]
    public void AddOns_resolve_row_overrides_against_catalog()
    {
        var currencyId = SimpleGuidGenerator.Instance.Create();
        var overrideCurrencyId = SimpleGuidGenerator.Instance.Create();
        var ribbonId = SimpleGuidGenerator.Instance.Create();
        var boxId = SimpleGuidGenerator.Instance.Create();
        var companyId = SimpleGuidGenerator.Instance.Create();

        var catalog = new Dictionary<Guid, AddOn>
        {
            [ribbonId] = new AddOn(companyId, "KURDELE", "Kurdele", currencyId, 25m, 1),
            [boxId] = new AddOn(companyId, "KUTU", "Hediye Kutusu", currencyId, 50m, 2),
        };
        var assignments = new List<ProductAddOn>
        {
            // DisplayOrder kasıtlı TERS verildi — çözümleyici sıraya göre dizmeli.
            new ProductAddOn(boxId, 60m, overrideCurrencyId, true, 2, "Özel not"),
            new ProductAddOn(ribbonId, null, null, false, 1, null),
        };

        var effective = ChannelInheritance.ResolveAddOns(assignments, catalog);

        effective.Count.ShouldBe(2);
        effective[0].ShouldBe(new EffectiveAddOn(ribbonId, "KURDELE", "Kurdele", 25m, currencyId, false, 1, null));
        effective[1].ShouldBe(new EffectiveAddOn(boxId, "KUTU", "Hediye Kutusu", 60m, overrideCurrencyId, true, 2, "Özel not"));
    }

    [Fact]
    public void AddOns_fail_fast_when_catalog_entry_is_missing()
    {
        var assignments = new List<ProductAddOn>
        {
            new ProductAddOn(SimpleGuidGenerator.Instance.Create(), null, null, false, 1, null),
        };

        // Sessiz atlama = kapsam düşürme (N11 push felsefesi) → katalogda olmayan referans fail-fast.
        var exception = Should.Throw<BusinessException>(
            () => ChannelInheritance.ResolveAddOns(assignments, new Dictionary<Guid, AddOn>()));
        exception.Code.ShouldBe("TradeXpress:Product:AddOnCatalogEntryMissing");
    }
}
