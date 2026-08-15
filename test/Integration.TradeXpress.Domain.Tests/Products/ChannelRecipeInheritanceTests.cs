using System;
using System.Collections.Generic;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// <see cref="ChannelRecipeInheritance"/> davranış testleri (saf birim — DB/DI YOK).
///
/// <para><b>Kilitlenen model</b> (2026-08-11 Hakan tasarımı): çekirdek varyant reçetesi OTORİTEDİR, kanal
/// reçetesi ondan TÜRER ve yalnız override hakkı vardır. "Override edildi mi" sorusu kalıcı bir bayrakla
/// değil, emtia İMZALARININ karşılaştırılmasıyla cevaplanır — bayrak, yalan söyleyebilen ikinci bir durum
/// olurdu.</para>
///
/// <para><b>En kritik iki assert:</b> ① yan maliyetlerin (paketleme/kargo/komisyon) karşılaştırmaya
/// GİRMEMESİ — her kanalda meşru şekilde farklıdırlar ve karışsalardı her kanal kalıcı olarak "override"
/// ilan edilir, devir mekanizması HİÇ çalışmazdı. ② aynı emtia kümesinde MİKTAR farkının override sayılması
/// — yalnız kimlik kümesi karşılaştırılsaydı kullanıcının bilinçli gramaj override'ı sessizce ezilirdi.</para>
/// </summary>
public class ChannelRecipeInheritanceTests
{
    private static readonly Guid MetalA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MetalB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MetalC = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid MetalD = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid GramUnit = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    public void Identical_commodity_signatures_are_inherited()
    {
        var core = new[] { Commodity(MetalA, 7m), Commodity(MetalB, 2m), Commodity(MetalC, 1m) };
        var channel = new[] { Commodity(MetalA, 7m), Commodity(MetalB, 2m), Commodity(MetalC, 1m) };

        ChannelRecipeInheritance.IsInherited(core, channel).ShouldBeTrue();
    }

    [Fact]
    public void An_extra_commodity_on_the_channel_means_the_variant_was_overridden()
    {
        // HAKAN'IN ÖRNEĞİ: çekirdek A+B+C, kanal A+B+C+D → kullanıcı kanalda bileşime dokunmuş.
        var core = new[] { Commodity(MetalA, 7m), Commodity(MetalB, 2m), Commodity(MetalC, 1m) };
        var channel = new[] { Commodity(MetalA, 7m), Commodity(MetalB, 2m), Commodity(MetalC, 1m), Commodity(MetalD, 3m) };

        ChannelRecipeInheritance.IsInherited(core, channel).ShouldBeFalse();
    }

    [Fact]
    public void A_different_quantity_on_the_same_commodity_set_means_the_variant_was_overridden()
    {
        // Yalnız emtia KİMLİKLERİ karşılaştırılsaydı bu satır "aynı küme" görünür ve kullanıcının bilinçli
        // 9 gramı bir sonraki tazelemede sessizce 7'ye dönerdi. İmza miktarı da kapsar.
        var core = new[] { Commodity(MetalA, 7m) };
        var channel = new[] { Commodity(MetalA, 9m) };

        ChannelRecipeInheritance.IsInherited(core, channel).ShouldBeFalse();
    }

    [Fact]
    public void Side_costs_never_take_part_in_the_comparison()
    {
        // HAKAN'IN ISRARLA AYIRDIĞI NOKTA: paketleme/kargo her kanalda farklı olabilir, komisyon zaten
        // kanalın kategorisinden gelir. Karışsalardı HER kanal kalıcı "override" olur, devir hiç çalışmazdı.
        var core = new[]
        {
            Commodity(MetalA, 7m),
            SideCost(SideCostKind.Packaging, 5m),
        };
        var channel = new[]
        {
            Commodity(MetalA, 7m),
            SideCost(SideCostKind.Packaging, 12m),      // farklı paketleme bedeli
            SideCost(SideCostKind.Cargo, 40m),          // kanala özel kargo
            SideCost(SideCostKind.Commission, 18.5m),   // kategoriden gelen komisyon
        };

        ChannelRecipeInheritance.IsInherited(core, channel).ShouldBeTrue();
    }

    [Fact]
    public void Core_labour_is_part_of_the_composition_and_is_compared()
    {
        // İŞÇİLİK ComponentType=Service'tir ama SideCostKind TAŞIMAZ → fiziksel bileşimin parçasıdır.
        // Ayrımı ComponentType üzerinden yapsaydık işçilik yanlışlıkla yan maliyet sayılır ve
        // kanaldaki farklı işçilik override olarak görülmezdi.
        var core = new[] { Commodity(MetalA, 7m), Labour(120m) };
        var channel = new[] { Commodity(MetalA, 7m), Labour(200m) };

        ChannelRecipeInheritance.IsInherited(core, channel).ShouldBeFalse();
    }

    [Fact]
    public void An_unclassified_variant_with_no_lines_on_either_side_counts_as_inherited()
    {
        // Devir mekanizmasının ASIL HEDEFİ bu durumdur: henüz sınıflandırılmamış ürün, üzerine yazılacak
        // bir kullanıcı kararı taşımaz. "Farklı" saysaydık emtia hiçbir zaman kanala akmazdı.
        ChannelRecipeInheritance.IsInherited(
            Array.Empty<IRecipeCommodityLine>(),
            Array.Empty<IRecipeCommodityLine>()).ShouldBeTrue();
    }

    [Fact]
    public void Losing_a_duplicate_line_is_an_override_even_though_the_commodity_set_is_unchanged()
    {
        // ÇOKLUK KORUNUR: aynı madenden iki ayrı satır (iki farklı gramaj) meşrudur; birini silmek
        // override'dır. Küme karşılaştırması bunu göremezdi — bu yüzden imza ÇOKLUĞU sayılıyor.
        var core = new[] { Commodity(MetalA, 7m), Commodity(MetalA, 7m), Commodity(MetalB, 1m) };
        var channel = new[] { Commodity(MetalA, 7m), Commodity(MetalB, 1m) };

        ChannelRecipeInheritance.IsInherited(core, channel).ShouldBeFalse();
    }

    [Fact]
    public void Line_order_and_description_do_not_affect_inheritance()
    {
        // Sıra kozmetiktir; imzaya girmez. Aksi halde kullanıcı satırları sürükleyince bileşim
        // "değişmiş" sayılır ve kanal çekirdekten kopardı.
        var core = new[] { Commodity(MetalA, 7m), Commodity(MetalB, 2m) };
        var channel = new[] { Commodity(MetalB, 2m), Commodity(MetalA, 7m) };

        ChannelRecipeInheritance.IsInherited(core, channel).ShouldBeTrue();
    }

    // ── Test ikizleri ───────────────────────────────────────────────────────────────────────────────

    private static IRecipeCommodityLine Commodity(Guid commodityId, decimal quantity)
    {
        return new FakeLine(ProcessType.Metal, commodityId, null, quantity, 1m, GramUnit, null);
    }

    private static IRecipeCommodityLine Labour(decimal amount)
    {
        // İşçilik: emtia kimliği yok, yan maliyet DEĞİL — miktar alanında ücret taşır.
        return new FakeLine(null, null, null, amount, 1m, null, null);
    }

    private static IRecipeCommodityLine SideCost(SideCostKind kind, decimal amount)
    {
        return new FakeLine(null, null, null, amount, 1m, null, kind);
    }

    private sealed record FakeLine(
        ProcessType? CommodityProcessType,
        Guid? CommodityId,
        Guid? CommodityVariantId,
        decimal Quantity,
        decimal Factor,
        Guid? ValuationUnitId,
        SideCostKind? SideCostKind) : IRecipeCommodityLine;
}
