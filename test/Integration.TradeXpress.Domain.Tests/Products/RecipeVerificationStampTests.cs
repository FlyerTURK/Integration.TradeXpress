using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Integration.TradeXpress.Products;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// <b>REÇETE DOĞRULAMA DAMGASI</b> — varyant onayının hâlâ geçerli olup olmadığını belirleyen saf hesap.
///
/// <para>Damga yanlış çalışırsa iki yönde de pahalı: fazla hassas olursa onay durmadan düşer (kullanıcı
/// bıkar, sonunda doğrulamayı ciddiye almaz), az hassas olursa reçete değişimi kaçar ve ürün YANLIŞ
/// FİYATLA satılır. Bu testler ikisini de kilitler.</para>
/// </summary>
public class RecipeVerificationStampTests
{
    /// <summary>ASIL KURAL 1 — içerik değişirse damga değişir. Kaçarsa reçete değişimi fark edilmez.</summary>
    [Fact]
    public void Changing_an_amount_changes_the_stamp()
    {
        var before = RecipeVerificationStamp.Compute(new[] { Line(amount: 5m) });
        var after = RecipeVerificationStamp.Compute(new[] { Line(amount: 6m) });

        RecipeVerificationStamp.Matches(before, after).ShouldBeFalse();
    }

    /// <summary>ASIL KURAL 2 — satıra DOKUNULUP aynı bırakıldıysa onay AYAKTA kalır.
    /// <para>Salt zaman damgası kullansaydık bu senaryo onayı boşuna düşürürdü: kullanıcı reçeteyi açıp
    /// hiçbir şey değiştirmeden kaydetse bile ürün satıştan çıkardı. İki kademeli damganın var oluş sebebi
    /// tam olarak budur.</para></summary>
    [Fact]
    public void Touching_a_line_without_changing_content_keeps_the_verification_valid()
    {
        var earlier = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc);
        var later = earlier.AddHours(3);

        var before = RecipeVerificationStamp.Compute(new[] { Line(changedUtc: earlier) });
        var after = RecipeVerificationStamp.Compute(new[] { Line(changedUtc: later) });

        // Zaman kısmı FARKLI...
        before.ShouldNotBe(after);

        // ...ama içerik aynı olduğu için onay geçerli kalır.
        RecipeVerificationStamp.Matches(before, after).ShouldBeTrue();
    }

    /// <summary>Satırların sırası değişse de damga aynı — salt yeniden sıralama onayı düşürmemeli.</summary>
    [Fact]
    public void Input_order_does_not_affect_the_stamp()
    {
        var a = Line(lineOrder: 0, commodityId: FixedCommodityA);
        var b = Line(lineOrder: 1, commodityId: FixedCommodityB);

        RecipeVerificationStamp.Compute(new[] { a, b })
            .ShouldBe(RecipeVerificationStamp.Compute(new[] { b, a }));
    }

    /// <summary>Aynı Guid FARKLI ailede geçiyorsa damga farklı olmalı — emtia ailesi kimliğin parçasıdır
    /// (CommodityId FK'sız snapshot, çakışma gerçek bir ihtimal).</summary>
    [Fact]
    public void Same_commodity_id_in_another_family_produces_a_different_stamp()
    {
        var metal = RecipeVerificationStamp.Compute(new[] { Line(family: 1) });
        var good = RecipeVerificationStamp.Compute(new[] { Line(family: 9) });

        RecipeVerificationStamp.Matches(metal, good).ShouldBeFalse();
    }

    /// <summary>
    /// KÜLTÜR TUZAĞI: ondalık ayracı kültüre göre değişirse aynı reçete iki makinede iki damga üretir —
    /// onay geliştirici makinesinde geçerli, sunucuda geçersiz olurdu. Sessiz ve teşhisi zor bir arıza.
    /// </summary>
    [Fact]
    public void Stamp_is_culture_invariant()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
            var turkish = RecipeVerificationStamp.Compute(new[] { Line(amount: 1.5m, factor: 0.995m) });

            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            var english = RecipeVerificationStamp.Compute(new[] { Line(amount: 1.5m, factor: 0.995m) });

            turkish.ShouldBe(english);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    /// <summary>Reçetesiz varyant da tutarlı kıyaslanabilmeli (boş liste sabit damga üretir).</summary>
    [Fact]
    public void Empty_recipe_has_a_stable_stamp()
    {
        RecipeVerificationStamp.Compute(Array.Empty<RecipeStampLine>())
            .ShouldBe(RecipeVerificationStamp.EmptyRecipe);
    }

    /// <summary>Damga yoksa (hiç doğrulanmamış) kıyas DAİMA false — "bilinmiyor" asla "geçerli" sayılmaz.</summary>
    [Fact]
    public void Missing_stamp_never_matches()
    {
        var current = RecipeVerificationStamp.Compute(new[] { Line() });

        RecipeVerificationStamp.Matches(null, current).ShouldBeFalse();
        RecipeVerificationStamp.Matches(current, null).ShouldBeFalse();
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────

    private static readonly Guid FixedCommodityA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FixedCommodityB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static RecipeStampLine Line(
        int lineOrder = 0,
        int componentType = 1,
        int? family = 1,
        Guid? commodityId = null,
        decimal quantity = 1m,
        decimal amount = 5m,
        decimal factor = 0.995m,
        DateTime? changedUtc = null)
    {
        return new RecipeStampLine(
            lineOrder,
            componentType,
            family,
            commodityId ?? FixedCommodityA,
            CommodityVariantId: null,
            quantity,
            amount,
            factor,
            changedUtc ?? new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc));
    }
}
