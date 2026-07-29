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

    // K10 testleri 2026-07-28'de kaldırıldı: test ettikleri zincir (PersonalizationValues +
    // ResolvePersonalization) Etsy'nin kapanan tek-kutulu modeline aitti. Kişiselleştirmenin yeni
    // taşıyıcısı SpecialInfo'dur; onun kanal-boşsa-ürün devralması ResolveList ile çalışır ve
    // "Lists_fall_back_to_product_when_channel_list_is_empty" testiyle zaten kapsanıyor.

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
