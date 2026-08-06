using System;
using System.Collections.Generic;
using Integration.TradeXpress.Orchestration;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// SellableStockCalculator — MOCK-FIRST test paketi (ADR: "tüm sistemler önce mock data ile"). Saf hesap,
/// DB'siz/DI'sız; senaryo sayıları 8GR.IAR.995 senaryosundan (2026-07-25 Hakan onaylı):
/// G1.0=18gr stok · G5.0=12gr gibi GERÇEK katalog örnekleriyle hizalı.
/// <para>2026-08-06: anahtar AİLE + ÖLÇÜM BOYUTU taşır. Maden davranışı bire bir korundu (aşağıdaki ilk
/// yedi test değişmedi, yalnız anahtar tipi genişledi); sonraki testler Good'u ve aile yalıtımını kilitler.</para>
/// </summary>
public class SellableStockCalculatorTests
{
    private static readonly Guid G1 = SimpleGuidGenerator.Instance.Create();   // 1.0 gr maden
    private static readonly Guid G5 = SimpleGuidGenerator.Instance.Create();   // 5.0 gr maden
    private static readonly Guid V1 = SimpleGuidGenerator.Instance.Create();   // G1'in bir varyantı

    [Fact]
    public void Bottleneck_metal_determines_sellable_count()
    {
        // Reçete: 1 ürün = 5gr'lık 1 parça (5.0) + 1gr'lık 3 parça (3.0). Stok: G5=12gr, G1=18gr.
        // Kapasite: G5 → 12/5=2, G1 → 18/3=6 → darboğaz G5 → 2 adet.
        var sellable = SellableStockCalculator.Calculate(
            new[] { Metal(G5, null, 5.0m), Metal(G1, null, 3.0m) },
            Stock((G5, null, 12m), (G1, null, 18m)));

        sellable.ShouldBe(2);
    }

    [Fact]
    public void Missing_metal_in_stock_means_zero_not_optimistic()
    {
        // Stok sözlüğünde G5 hiç yok → 0 kabul edilir → satılabilir adet 0 (oversell kapısı kapalı).
        var sellable = SellableStockCalculator.Calculate(
            new[] { Metal(G5, null, 5.0m) },
            Stock());

        sellable.ShouldBe(0);
    }

    [Fact]
    public void Negative_net_stock_clamps_to_zero()
    {
        // Fazla çıkışla eksiye düşmüş stok → kanala asla eksi gitmez, 0'a kırpılır.
        var sellable = SellableStockCalculator.Calculate(
            new[] { Metal(G1, null, 1.0m) },
            Stock((G1, null, -4m)));

        sellable.ShouldBe(0);
    }

    [Fact]
    public void No_commodity_lines_returns_null_meaning_not_stock_bound()
    {
        // Reçetede emtia satırı yok (hizmet/manuel) → null: kanal stoğuna dokunulmaz.
        var sellable = SellableStockCalculator.Calculate(
            Array.Empty<RecipeCommodityRequirement>(),
            Stock());

        sellable.ShouldBeNull();
    }

    [Fact]
    public void Zero_requirement_lines_do_not_constrain()
    {
        // İhtiyacı 0 olan satır (bilgi satırı) kapasiteyi kısıtlamaz; kalan satırdan 3 çıkar.
        var sellable = SellableStockCalculator.Calculate(
            new[] { Metal(G5, null, 0m), Metal(G1, null, 6.0m) },
            Stock((G1, null, 18m)));

        sellable.ShouldBe(3);
    }

    [Fact]
    public void Variant_specific_stock_wins_over_total_when_present()
    {
        // Varyantlı satır: (G1,V1) anahtarı VARSA o esas (6gr → 2 adet); toplam 18gr olsa bile.
        var sellable = SellableStockCalculator.Calculate(
            new[] { Metal(G1, V1, 3.0m) },
            Stock((G1, V1, 6m), (G1, null, 18m)));

        sellable.ShouldBe(2);
    }

    [Fact]
    public void Variant_line_falls_back_to_total_when_variant_key_absent()
    {
        // Varyant anahtarı stokta yoksa (varyantsız takip) toplam kullanılır → 18/3 = 6.
        var sellable = SellableStockCalculator.Calculate(
            new[] { Metal(G1, V1, 3.0m) },
            Stock((G1, null, 18m)));

        sellable.ShouldBe(6);
    }

    [Fact]
    public void Fractional_capacity_floors_to_whole_units()
    {
        // 8gr hedefli senaryo satırı: 17gr stok / 8gr ihtiyaç = 2.125 → TABAN 2 (yarım paket satılamaz).
        var sellable = SellableStockCalculator.Calculate(
            new[] { Metal(G5, null, 8.0m) },
            Stock((G5, null, 17m)));

        sellable.ShouldBe(2);
    }

    // ── Aile genişlemesi (2026-08-06) ───────────────────────────────────────────────────────────────

    /// <summary>Mamül ADETLE kısıtlar: 7 adet elde, 2 adet ihtiyaç → 3.</summary>
    [Fact]
    public void Good_line_constrains_on_quantity()
    {
        var goodId = SimpleGuidGenerator.Instance.Create();

        var sellable = SellableStockCalculator.Calculate(
            new[] { Good(goodId, null, requiredQuantity: 2m) },
            new Dictionary<CommodityStockKey, CommodityAvailability>
            {
                [new CommodityStockKey(ProcessType.Good, goodId, null)] = new(Amount: 999m, Quantity: 7m),
            });

        sellable.ShouldBe(3);
    }

    /// <summary>Satır İKİ boyutu birden beyan ederse ikisi de kısıttır ve DAR olan kazanır.
    /// <para>Tek boyuta indirgeyip diğerini varsaymak, bu oturumun avladığı sessiz-yanlış-rakam deseniydi:
    /// "3 adet var" deyip 1,5 kg ihtiyacını görmemek elimizde olmayan malı satılabilir gösterirdi.</para></summary>
    [Fact]
    public void Good_line_declaring_both_dimensions_is_limited_by_the_tighter_one()
    {
        var goodId = SimpleGuidGenerator.Instance.Create();

        var sellable = SellableStockCalculator.Calculate(
            new[]
            {
                new RecipeCommodityRequirement(
                    ProcessType.Good, goodId, null, RequiredAmountPerUnit: 1.5m, RequiredQuantityPerUnit: 2m),
            },
            new Dictionary<CommodityStockKey, CommodityAvailability>
            {
                // Adet 10/2 = 5 verirdi; miktar 3/1,5 = 2 → darboğaz miktar.
                [new CommodityStockKey(ProcessType.Good, goodId, null)] = new(Amount: 3m, Quantity: 10m),
            });

        sellable.ShouldBe(2);
    }

    /// <summary>AYNI Guid iki ailede AYRI havuzdur — <c>CommodityId</c> FK'sız snapshot olduğundan çakışma
    /// gerçek bir ihtimaldir. Aile anahtarda olmasaydı maden stoğu mamül ihtiyacını karşılıyor görünürdü.</summary>
    [Fact]
    public void Same_id_in_two_families_stays_in_separate_pools()
    {
        var sharedId = SimpleGuidGenerator.Instance.Create();

        var sellable = SellableStockCalculator.Calculate(
            new[] { Good(sharedId, null, requiredQuantity: 1m) },
            new Dictionary<CommodityStockKey, CommodityAvailability>
            {
                // Aynı Guid, ama MADEN ailesinde — mamül talebini karşılamaz.
                [new CommodityStockKey(ProcessType.Metal, sharedId, null)] = new(Amount: 500m, Quantity: 500m),
            });

        sellable.ShouldBe(0);
    }

    /// <summary>Çok aileli reçetede darboğaz hangi aileden gelirse gelsin aynı min'e girer.</summary>
    [Fact]
    public void Mixed_family_recipe_takes_the_minimum_across_families()
    {
        var goodId = SimpleGuidGenerator.Instance.Create();

        var sellable = SellableStockCalculator.Calculate(
            new[] { Metal(G5, null, 5.0m), Good(goodId, null, requiredQuantity: 1m) },
            new Dictionary<CommodityStockKey, CommodityAvailability>
            {
                [new CommodityStockKey(ProcessType.Metal, G5, null)]     = new(Amount: 50m, Quantity: 10m),
                [new CommodityStockKey(ProcessType.Good, goodId, null)]  = new(Amount: 0m, Quantity: 4m),
            });

        // Maden 50/5 = 10; mamül 4/1 = 4 → 4.
        sellable.ShouldBe(4);
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Maden satırı — YALNIZ gram kısıtlar (adet boyutu maden için bilinçli olarak 0'dır).</summary>
    private static RecipeCommodityRequirement Metal(Guid metalId, Guid? variantId, decimal grams)
    {
        return new RecipeCommodityRequirement(ProcessType.Metal, metalId, variantId, grams, 0m);
    }

    private static RecipeCommodityRequirement Good(Guid goodId, Guid? variantId, decimal requiredQuantity)
    {
        return new RecipeCommodityRequirement(ProcessType.Good, goodId, variantId, 0m, requiredQuantity);
    }

    /// <summary>Maden stoğu — gram boyutunda (adet okunmaz).</summary>
    private static Dictionary<CommodityStockKey, CommodityAvailability> Stock(
        params (Guid MetalId, Guid? VariantId, decimal Grams)[] rows)
    {
        var result = new Dictionary<CommodityStockKey, CommodityAvailability>();
        foreach (var row in rows)
        {
            result[new CommodityStockKey(ProcessType.Metal, row.MetalId, row.VariantId)] =
                new CommodityAvailability(row.Grams, 0m);
        }

        return result;
    }
}
