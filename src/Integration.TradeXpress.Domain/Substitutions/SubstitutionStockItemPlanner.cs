using System.Globalization;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil kombinasyon → kanal StockItem dönüşümünün SAF PLANLAYICISI (DB'siz, repository'siz, DI'sız;
/// <c>SubstitutionSolver</c>/<c>VariantCombinationEngine</c> kardeşi). Başarılı kombinasyon listesinden
/// kanal-agnostik plan kayıtları üretir: değer metni + nötr imza bileşenleri + reçete satırları + paket + Rank.
/// Kanal adaptörleri (N11/Trendyol) bu planı kendi graf tiplerinde uygular — matematik/metin üretimi TEK yerde.
/// <para><b>Ticari tolerans bildirimi</b> (konsept madde 3): tolerans &gt; 0 olan grupla üretilen planın
/// <see cref="SubstitutionStockItemPlan.ToleranceNotice"/> metni push AÇIKLAMASINA iliştirilmek üzere burada
/// üretilir (push entegrasyonu ayrı dilim). Metin Türkçe ticari beyandır (TR pazaryerleri müşterisine dönük)
/// — UI lokalizasyon kaynağına bağlı DEĞİLDİR.</para>
/// </summary>
public static class SubstitutionStockItemPlanner
{
    // Müşteriye dönük metinler (değer metni gramaj + tolerans notu) TR pazaryeri formatındadır (ondalık virgül).
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    public static SubstitutionStockItemPlan Build(SubstitutionStockItemPlanInput input)
    {
        Check.NotNull(input, nameof(input));

        var selected = SelectTopCombinations(input);
        if (selected.Count == 0)
        {
            // Hesap yalnız başarısız üretti (ya da hiç kombinasyon yok) → varyant kurulamaz (bağlayıcı karar 7).
            throw new BusinessException("TradeXpress:Substitution:NoSuccessfulCombination");
        }

        var valueTexts = BuildUniqueValueTexts(selected);

        var items = new List<SubstitutionStockItemPlanItem>(selected.Count);
        for (var i = 0; i < selected.Count; i++)
        {
            var combination = selected[i];
            items.Add(new SubstitutionStockItemPlanItem(
                combination.Rank,
                IsPrimary: i == 0,   // Rank artan sıralı → ilk kayıt = en iyi skor = ANA varyant (karar 2)
                valueTexts[i],
                BuildPlanKey(combination),
                combination.PackageCount,
                ImageUrl: null,      // AI kombinasyon görseli projesinin bağlanma noktası — şimdilik daima null (karar 6)
                combination.Lines.Select(BuildRecipeLine).ToList()));
        }

        return new SubstitutionStockItemPlan(
            BuildToleranceNotice(input.ToleranceType, input.ToleranceValue),
            items);
    }

    /// <summary>Ticari tolerans bildirimi (konsept madde 3, kullanıcı kararı): tolerans &gt; 0 ise
    /// türe göre "+/− binde {x} tolerans hakkı saklıdır" (PerMille) ya da
    /// "+/− {x} gram tolerans hakkı saklıdır" (Gram); 0 → null (bildirim yok).</summary>
    public static string? BuildToleranceNotice(ToleranceType toleranceType, decimal toleranceValue)
    {
        if (toleranceValue <= 0m)
        {
            return null;
        }

        var value = FormatDecimal(toleranceValue);
        if (toleranceType == ToleranceType.PerMille)
        {
            return $"+/− binde {value} tolerans hakkı saklıdır";
        }

        return $"+/− {value} gram tolerans hakkı saklıdır";
    }

    /// <summary>Rank artan sıralar + Top-N keser (≤0 → tümü). Rank'sız/satırsız kayıt fail-fast —
    /// planlayıcıya yalnız BAŞARILI kombinasyon girer (besleme hatasını sessiz geçme).</summary>
    private static List<SubstitutionPlanCombination> SelectTopCombinations(SubstitutionStockItemPlanInput input)
    {
        foreach (var combination in input.SuccessfulCombinations)
        {
            if (combination.Rank <= 0 || combination.Lines.Count == 0)
            {
                throw new BusinessException("TradeXpress:Substitution:NoSuccessfulCombination");
            }
        }

        var limit = input.TopN > 0 ? input.TopN : input.SuccessfulCombinations.Count;
        return input.SuccessfulCombinations
            .OrderBy(c => c.Rank)
            .Take(limit)
            .ToList();
    }

    /// <summary>Değer metinleri — kademeli ayrıştırma: (1) gramaj ("1×10gr + 2×1gr"); (2) aynı metni üreten iki plan
    /// maden ADIYLA; (3) hâlâ aynıysa VARYANT KODUYLA; (4) son çare Rank son-eki. Kanal özellik değerleri metin
    /// bazında benzersiz kalmalı (aynı değer = aynı kombinasyon yorumu).
    /// <para><b>Varyant basamağı neden ŞART (kod-inceleme bulgusu):</b> Dilim-2'den beri iki kombinasyon YALNIZ metal
    /// varyantında farklı olabiliyor (aynı maden adı, aynı PieceWeight) → ilk iki basamak da çöküyordu ve ayrım tek
    /// başına Rank son-ekine kalıyordu. Rank ise CANLI veriden türer (maliyet + paket sayısı): bir işçilik düzenlemesi
    /// ya da sıradan bir satış sıralamayı takas ediyor, metin çoklu-kümesi aynı kaldığı için kanal eşleştirmesi
    /// (DiffCombinationValues, normalize metin bazlı) hiçbir fark görmüyor ve CANLI bir pazaryeri seçeneğinin
    /// reçetesi/stoğu KARŞI kombinasyonla eziliyordu — SKU ve sipariş geçmişi aynı, gönderilen mal farklı.
    /// Varyant kodu kimliği STABİL kılar (rank'tan bağımsız) ve alıcıya görünen metni de anlamlandırır
    /// ("2×ÇEYREK 1,75gr (ESKİ-KULPLU)" — "… #2" yerine).</para></summary>
    private static List<string> BuildUniqueValueTexts(List<SubstitutionPlanCombination> selected)
    {
        var texts = selected.Select(c => BuildValueText(c, includeMetalName: false, includeVariant: false)).ToList();

        ResolveDuplicates(texts, i => BuildValueText(selected[i], includeMetalName: true, includeVariant: false));
        ResolveDuplicates(texts, i => BuildValueText(selected[i], includeMetalName: true, includeVariant: true));

        // Son çare: varyant boyutu da ayırmadıysa (varyantsız legacy adaylar) Rank benzersizdir.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < texts.Count; i++)
        {
            if (!seen.Add(texts[i]))
            {
                texts[i] = $"{texts[i]} #{selected[i].Rank}";
                seen.Add(texts[i]);
            }
        }

        return texts;
    }

    /// <summary>Hâlâ çakışan metinleri bir sonraki ayrıştırma basamağıyla yeniden üretir (yerinde).</summary>
    private static void ResolveDuplicates(List<string> texts, Func<int, string> rebuild)
    {
        var duplicated = texts
            .GroupBy(t => t, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (duplicated.Count == 0)
        {
            return;
        }

        for (var i = 0; i < texts.Count; i++)
        {
            if (duplicated.Contains(texts[i]))
            {
                texts[i] = rebuild(i);
            }
        }
    }

    private static string BuildValueText(
        SubstitutionPlanCombination combination, bool includeMetalName, bool includeVariant)
    {
        var segments = combination.Lines.Select(l =>
        {
            var weight = FormatDecimal(l.PieceWeight);
            var body = includeMetalName
                ? $"{l.Count}×{l.MetalName} {weight}gr"
                : $"{l.Count}×{weight}gr";

            // Varyant kodu yalnız gerektiğinde ve VARSA eklenir (varyantsız legacy satır metnini değiştirmez).
            return includeVariant && !string.IsNullOrWhiteSpace(l.VariantCode)
                ? $"{body} ({l.VariantCode})"
                : body;
        });

        return string.Join(" + ", segments);
    }

    /// <summary>Nötr imza bileşenleri — "{MetalId}x{Count}|..." MetalId artan sıralı (sıra-bağımsız
    /// deterministik; kanal CombinationSignature'ı DEĞİL — kanal kendi kuralıyla üretir). Varyantlı satır
    /// "{MetalId}:{VariantId}x{Count}" biçimiyle ayrışır — aynı madenin iki varyantı aynı kombinasyonda
    /// bile çakışmaz; varyantsız (legacy) satır ESKİ biçimi korur (anahtar statükosu).</summary>
    private static string BuildPlanKey(SubstitutionPlanCombination combination)
    {
        return string.Join('|', combination.Lines
            .OrderBy(l => l.MetalId)
            .ThenBy(l => l.VariantId ?? Guid.Empty)
            .Select(l => l.VariantId is { } variantId
                ? $"{l.MetalId}:{variantId}x{l.Count}"
                : $"{l.MetalId}x{l.Count}"));
    }

    private static SubstitutionPlanRecipeLine BuildRecipeLine(SubstitutionPlanCombinationLine line)
    {
        return new SubstitutionPlanRecipeLine(
            line.MetalId, line.Count, line.PieceWeight, line.Count * line.PieceWeight,
            line.VariantId, line.VariantCode);
    }

    /// <summary>Ondalık formatı — sondaki sıfırlar atılır, TR ondalık virgül (müşteriye dönük metin).</summary>
    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.#####", TurkishCulture);
    }
}
