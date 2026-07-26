using System;
using System.Collections.Generic;
using Integration.TradeXpress.Orchestration;
using Shouldly;
using Volo.Abp.Guids;
using Xunit;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// SellableStockCalculator — MOCK-FIRST test paketi (ADR: "tüm sistemler önce mock data ile"). Saf hesap,
/// DB'siz/DI'sız; senaryo sayıları 8GR.IAR.995 senaryosundan (2026-07-25 Hakan onaylı):
/// G1.0=18gr stok · G5.0=12gr gibi GERÇEK katalog örnekleriyle hizalı.
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
            new[]
            {
                new RecipeMetalRequirement(G5, null, 5.0m),
                new RecipeMetalRequirement(G1, null, 3.0m),
            },
            new Dictionary<(Guid, Guid?), decimal> { [(G5, null)] = 12m, [(G1, null)] = 18m });

        sellable.ShouldBe(2);
    }

    [Fact]
    public void Missing_metal_in_stock_means_zero_not_optimistic()
    {
        // Stok sözlüğünde G5 hiç yok → 0 kabul edilir → satılabilir adet 0 (oversell kapısı kapalı).
        var sellable = SellableStockCalculator.Calculate(
            new[] { new RecipeMetalRequirement(G5, null, 5.0m) },
            new Dictionary<(Guid, Guid?), decimal>());

        sellable.ShouldBe(0);
    }

    [Fact]
    public void Negative_net_stock_clamps_to_zero()
    {
        // Fazla çıkışla eksiye düşmüş stok → kanala asla eksi gitmez, 0'a kırpılır.
        var sellable = SellableStockCalculator.Calculate(
            new[] { new RecipeMetalRequirement(G1, null, 1.0m) },
            new Dictionary<(Guid, Guid?), decimal> { [(G1, null)] = -4m });

        sellable.ShouldBe(0);
    }

    [Fact]
    public void No_metal_lines_returns_null_meaning_not_stock_bound()
    {
        // Reçetede metal satırı yok (hizmet/manuel) → null: kanal stoğuna dokunulmaz.
        var sellable = SellableStockCalculator.Calculate(
            Array.Empty<RecipeMetalRequirement>(),
            new Dictionary<(Guid, Guid?), decimal>());

        sellable.ShouldBeNull();
    }

    [Fact]
    public void Zero_requirement_lines_do_not_constrain()
    {
        // İhtiyacı 0 olan satır (bilgi satırı) kapasiteyi kısıtlamaz; kalan satırdan 3 çıkar.
        var sellable = SellableStockCalculator.Calculate(
            new[]
            {
                new RecipeMetalRequirement(G5, null, 0m),
                new RecipeMetalRequirement(G1, null, 6.0m),
            },
            new Dictionary<(Guid, Guid?), decimal> { [(G1, null)] = 18m });

        sellable.ShouldBe(3);
    }

    [Fact]
    public void Variant_specific_stock_wins_over_total_when_present()
    {
        // Varyantlı satır: (G1,V1) anahtarı VARSA o esas (6gr → 2 adet); toplam 18gr olsa bile.
        var sellable = SellableStockCalculator.Calculate(
            new[] { new RecipeMetalRequirement(G1, V1, 3.0m) },
            new Dictionary<(Guid, Guid?), decimal> { [(G1, V1)] = 6m, [(G1, null)] = 18m });

        sellable.ShouldBe(2);
    }

    [Fact]
    public void Variant_line_falls_back_to_total_when_variant_key_absent()
    {
        // Varyant anahtarı stokta yoksa (varyantsız takip) toplam kullanılır → 18/3 = 6.
        var sellable = SellableStockCalculator.Calculate(
            new[] { new RecipeMetalRequirement(G1, V1, 3.0m) },
            new Dictionary<(Guid, Guid?), decimal> { [(G1, null)] = 18m });

        sellable.ShouldBe(6);
    }

    [Fact]
    public void Fractional_capacity_floors_to_whole_units()
    {
        // 8gr hedefli senaryo satırı: 17gr stok / 8gr ihtiyaç = 2.125 → TABAN 2 (yarım paket satılamaz).
        var sellable = SellableStockCalculator.Calculate(
            new[] { new RecipeMetalRequirement(G5, null, 8.0m) },
            new Dictionary<(Guid, Guid?), decimal> { [(G5, null)] = 17m });

        sellable.ShouldBe(2);
    }
}
