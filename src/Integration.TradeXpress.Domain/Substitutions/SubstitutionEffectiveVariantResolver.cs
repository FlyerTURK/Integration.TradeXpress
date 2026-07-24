namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil grubu kaleminin ETKİN VARYANT KÜMESİ çözümleyicisi — SAF STATİK (DB'siz/DI'sız;
/// <see cref="SubstitutionSolver"/> kardeşi). Öncelik zinciri (bağlayıcı tasarım, Dilim-2):
/// <c>override ?? IncludedVariantIds(doluysa) ?? {ana varyant}</c>.
/// <list type="number">
///   <item><b>Override</b> (Dilim-3 ürün-düzeyi override'ı için parametreli; bu dilimde çağıranlar null geçer):
///   dolu geldiyse kalem ayarını tamamen ezer.</item>
///   <item><b>IncludedVariantIds</b> (kalem opt-in kümesi): dolu ise etkin küme odur — sıra kullanıcı-kontrollü
///   (aday sırası = tüketim önceliği içi alt-sıra), duplike/boş-Guid savunmacı ayıklanır.</item>
///   <item><b>Boş küme = yalnız ANA varyant</b> (statüko değişmezi): ana varyant id'si döner; madenin hiç
///   katalog varyantı yoksa TEK null eleman döner (legacy "varyantsız maden" adayı — stok null-varyant
///   havuzunda, işçilik sessiz-0 yolunda akmaya devam eder).</item>
/// </list>
/// Katalog AİDİYET doğrulaması (id gerçekten o madenin varyantı mı) burada DEĞİL — çağıran (besleme)
/// katalogla karşılaştırıp fail-fast eder; çözümleyici saf kalır.
/// </summary>
public static class SubstitutionEffectiveVariantResolver
{
    /// <summary>Etkin varyant kümesini çözer — dönen liste sırası aday sırasıdır; tek null eleman
    /// "katalog varyantı olmayan maden" (legacy) adayını temsil eder.</summary>
    public static IReadOnlyList<Guid?> Resolve(
        IReadOnlyList<Guid>? overrideVariantIds,
        IReadOnlyList<Guid>? includedVariantIds,
        Guid? mainVariantId)
    {
        var overrides = Normalize(overrideVariantIds);
        if (overrides.Count > 0)
        {
            return overrides;
        }

        var included = Normalize(includedVariantIds);
        if (included.Count > 0)
        {
            return included;
        }

        return new List<Guid?> { mainVariantId };
    }

    // Savunmacı normalizasyon (entity SetIncludedVariants ile aynı sözleşme): boş-Guid ayıklanır,
    // duplike düşer, KULLANICI SIRASI korunur; null/boş girdi = "yok" (fall-through).
    private static List<Guid?> Normalize(IReadOnlyList<Guid>? variantIds)
    {
        var result = new List<Guid?>();
        if (variantIds is null)
        {
            return result;
        }

        var seen = new HashSet<Guid>();
        foreach (var id in variantIds)
        {
            if (id != Guid.Empty && seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }
}
