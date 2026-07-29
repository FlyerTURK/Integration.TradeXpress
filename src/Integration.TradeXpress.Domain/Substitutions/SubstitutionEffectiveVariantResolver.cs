namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil grubu kaleminin ETKİN VARYANT KÜMESİ çözümleyicisi — SAF STATİK (DB'siz/DI'sız;
/// <see cref="SubstitutionSolver"/> kardeşi). Öncelik: <c>override(VERİLDİYSE) → IncludedVariantIds(doluysa)
/// → {ana varyant}</c>.
/// <list type="number">
///   <item><b>Override</b> (ürün-düzeyi kapsam): <c>null</c> = ürün bağlamı yok (grup modu). <c>null değil</c>
///   ise ürünün KENDİ kapsamıdır ve tek doğrudur — <b>boş liste dahil</b> ("bu madeni istemiyorum").
///   Ürün gruba bağlanırken grup kapsamı ürüne kopyalandığından, sonrasında grup bu üründe belirleyici
///   değildir (2026-07-27 kararı; öncesinde boş override sessizce gruba dönüyor, kullanıcının kaldırma
///   eylemini etkisiz kılıyordu).</item>
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
        // ÜRÜN MODU — liste VERİLDİYSE (null değil) tek doğru odur; BOŞ olması da bir cevaptır:
        // "bu madeni istemiyorum". Ürün gruba bağlanırken grubun kapsamı ürüne KOPYALANDIĞI için
        // (materyalizasyon) sonrasında grubun bu üründe işi kalmaz — kullanıcı işareti kaldırdığında
        // sessizce gruba geri dönmek, kaldırma eylemini etkisiz kılıyordu (2026-07-27 Hakan kararı).
        if (overrideVariantIds is not null)
        {
            return Normalize(overrideVariantIds);
        }

        // GRUP MODU — kalemin kendi opt-in kümesi; boşsa statüko değişmezi: yalnız ana varyant.
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
