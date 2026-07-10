using System.Globalization;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil kombinasyon motoru — SAF STATİK (DB'siz, repository'siz, DI'sız; <c>VariantCombinationEngine</c>
/// kardeşi). SSOT: .claude/research/muadil/konsept.md — kullanıcının çalışılmış 12gr örneği birebir.
/// <para><b>Algoritma:</b> (1) ön-filtre (tek parçası talep+toleransı aşan ya da stoksuz emtia elenir,
/// ayrı raporlanır); (2) TAM numaralandırma = sıra-öncelikli açgözlü doldurma + sistematik geri-izleme:
/// ilk kolondan başla, her kolona kalan ihtiyaca sığan MAKSİMUM adedi yaz (stok sınırlı), değerlendir;
/// sonra SON dolu kolondan 1 eksilt, sonraki kolonları yeniden açgözlü doldur… TÜM kolonlar 0'a inene
/// kadar (azalan-leksikografik tam sıralama — İSTİSNA: dizinin SON kolonunda birer eksiltme yapılmaz,
/// bkz. EnumerateAllCombinations son-kolon kuralı); (3) geçerlilik:
/// |toplam − talep| ≤ efektif tolerans; (4) skor (yalnız başarılılar, lexicographic): maliyet KÜÇÜK →
/// parça KÜÇÜK → paket BÜYÜK → Rank (1 = ana varyant adayı).</para>
/// <para><b>Limit YOK (2026-07-10 kullanıcı kararı):</b> numaralandırma HER ZAMAN sonuna kadar gider —
/// deneme sayısı sınırı kaldırıldı ("bininci kombinasyon belki en iyi kombinasyon olabilir"). Ön-filtre +
/// sonlu stok arama uzayını zaten sonlu tutar; erken kesim en iyi kombinasyonu kaçırabilirdi.</para>
/// </summary>
public static class SubstitutionSolver
{
    // Teknik neden kodları Domain.Shared'daki SubstitutionReasonCodes'ta (SSOT) — UI/testler de aynı sabitleri kullanır.

    public static SubstitutionSolverResult Solve(SubstitutionSolverInput input)
    {
        Check.NotNull(input, nameof(input));
        EnsureInputValid(input);

        var tolerance = ResolveEffectiveTolerance(input);
        var upperLimit = input.RequestedAmount + tolerance;

        var (commodities, filteredOut) = PreFilter(input.Commodities, upperLimit);
        if (commodities.Count == 0)
        {
            return new SubstitutionSolverResult(new List<SubstitutionCombination>(), filteredOut);
        }

        // Toplam-kapasite kısa devresi (2026-07-10 kullanıcı kararı): envanterin toplam ağırlığı talebin
        // (tolerans alt bandının) altındaysa hiçbir kombinasyon tutamaz — numaralandırma HİÇ başlatılmaz.
        var totalAvailableWeight = commodities.Sum(c => c.AvailableCount * c.PieceWeight);
        if (totalAvailableWeight < input.RequestedAmount - tolerance)
        {
            return new SubstitutionSolverResult(
                new List<SubstitutionCombination>(), filteredOut, totalAvailableWeight, InsufficientStock: true);
        }

        var all = EnumerateAllCombinations(input.RequestedAmount, tolerance, upperLimit, commodities);
        AssignRanks(all);

        return new SubstitutionSolverResult(all, filteredOut, totalAvailableWeight);
    }

    /// <summary>Efektif tolerans — Gram: değer aynen; PerMille: talep × değer / 1000 (konsept madde 3).</summary>
    private static decimal ResolveEffectiveTolerance(SubstitutionSolverInput input)
    {
        if (input.ToleranceType == ToleranceType.PerMille)
        {
            return input.RequestedAmount * input.ToleranceValue / 1000m;
        }

        return input.ToleranceValue;
    }

    private static void EnsureInputValid(SubstitutionSolverInput input)
    {
        if (input.RequestedAmount <= 0m)
        {
            throw new BusinessException("TradeXpress:Substitution:RequestedAmountInvalid");
        }

        if (input.ToleranceValue < 0m)
        {
            throw new BusinessException("TradeXpress:Substitution:ToleranceValueInvalid");
        }

        if (input.Commodities.Any(c => c.PieceWeight <= 0m))
        {
            throw new BusinessException("TradeXpress:Substitution:PieceWeightInvalid");
        }
    }

    /// <summary>Ön-filtre (konsept madde 4): stoksuzlar + tek parçası talep+toleransı aşanlar elenir;
    /// elenenler ayrı raporlanır. Sıra (tüketim önceliği) korunur.</summary>
    private static (List<SubstitutionCommodity> Kept, List<SubstitutionFilteredCommodity> FilteredOut) PreFilter(
        IReadOnlyList<SubstitutionCommodity> commodities,
        decimal upperLimit)
    {
        var kept = new List<SubstitutionCommodity>();
        var filteredOut = new List<SubstitutionFilteredCommodity>();

        foreach (var commodity in commodities)
        {
            if (commodity.AvailableCount <= 0)
            {
                filteredOut.Add(new SubstitutionFilteredCommodity(
                    commodity.Id, commodity.Code, SubstitutionReasonCodes.NoStock));
                continue;
            }

            if (commodity.PieceWeight > upperLimit)
            {
                filteredOut.Add(new SubstitutionFilteredCommodity(
                    commodity.Id, commodity.Code, SubstitutionReasonCodes.PieceWeightExceedsTarget));
                continue;
            }

            kept.Add(commodity);
        }

        return (kept, filteredOut);
    }

    /// <summary>Tam numaralandırma — azalan-leksikografik sıra: açgözlü doldur → değerlendir →
    /// son dolu kolondan 1 eksilt → sonrakileri yeniden doldur… tüm kolonlar 0'a inene kadar.
    /// Açgözlü tavan (kalan ihtiyaç + tolerans) aşan kombinasyonları hiç üretmez (aşma zaten geçersiz).
    /// <para><b>Son-kolon kuralı (2026-07-10 kullanıcı kararı):</b> dizinin SON kolonunda birer eksiltme
    /// YAPILMAZ — son kolon kalan miktarı ya tek seferde karşılar ya da dal kapanıp bir önceki kolona
    /// geçilir (ara adetler matematiksel olarak asla tutamaz; kullanıcının 12gr tablosu bu kuralla 27
    /// denemedir). Tolerans &gt; 0'da bandın İÇİNDE kalan alt adetler ayrı geçerli kombinasyonlardır —
    /// yalnız onlar üretilir; bandın altına inen ilk adetle dal kapanır.</para></summary>
    private static List<SubstitutionCombination> EnumerateAllCombinations(
        decimal requestedAmount,
        decimal tolerance,
        decimal upperLimit,
        List<SubstitutionCommodity> commodities)
    {
        var all = new List<SubstitutionCombination>();
        var counts = new int[commodities.Count];
        var lastColumn = commodities.Count - 1;

        GreedyFillFrom(0);
        while (true)
        {
            var lastNonZero = FindLastNonZero();
            if (lastNonZero < 0)
            {
                // All-zero (boş) dizilim deneme sayılmaz — numaralandırma orada biter.
                break;
            }

            var trial = RecordTrial();

            if (lastNonZero == lastColumn)
            {
                // Son kolon: açgözlü dolum başarılıysa tolerans bandında kalan alt adetler de üretilir;
                // ilk band-altı adetle (ya da dolum zaten başarısızsa hemen) dal kapanır — birer birer
                // sıfıra yürünmez ("bir kerede karşılıyorsa karşılar, karşılamıyorsa sonraki kombinasyon").
                while (trial.Success && counts[lastColumn] > 1)
                {
                    counts[lastColumn]--;
                    var lower = BuildCombination(requestedAmount, tolerance, commodities, counts);
                    if (!lower.Success)
                    {
                        break;   // bandın altına indi — daha küçük adetler de tutmaz
                    }

                    all.Add(lower);
                    trial = lower;
                }

                counts[lastColumn] = 0;
                var previousNonZero = FindLastNonZero();
                if (previousNonZero < 0)
                {
                    break;
                }

                counts[previousNonZero]--;
                GreedyFillFrom(previousNonZero + 1);
                continue;
            }

            counts[lastNonZero]--;
            GreedyFillFrom(lastNonZero + 1);
        }

        return all;

        // Kolonlara soldan sağa, kalan ihtiyaca (talep + tolerans tavanı) sığan MAKSİMUM adedi yaz (stok sınırlı).
        void GreedyFillFrom(int startIndex)
        {
            var total = 0m;
            for (var i = 0; i < startIndex; i++)
            {
                total += counts[i] * commodities[i].PieceWeight;
            }

            for (var i = startIndex; i < commodities.Count; i++)
            {
                var capacity = (upperLimit - total) / commodities[i].PieceWeight;
                var byNeed = capacity >= commodities[i].AvailableCount
                    ? commodities[i].AvailableCount
                    : (int)Math.Floor(capacity);
                counts[i] = byNeed > 0 ? byNeed : 0;
                total += counts[i] * commodities[i].PieceWeight;
            }
        }

        int FindLastNonZero()
        {
            for (var i = counts.Length - 1; i >= 0; i--)
            {
                if (counts[i] > 0)
                {
                    return i;
                }
            }

            return -1;
        }

        SubstitutionCombination RecordTrial()
        {
            var combination = BuildCombination(requestedAmount, tolerance, commodities, counts);
            all.Add(combination);
            return combination;
        }
    }

    /// <summary>Tek denemeyi değerlendirir — geçerlilik (konsept madde 3) + maliyet/parça/paket ölçümleri.</summary>
    private static SubstitutionCombination BuildCombination(
        decimal requestedAmount,
        decimal tolerance,
        List<SubstitutionCommodity> commodities,
        int[] counts)
    {
        var lines = new List<(Guid CommodityId, int Count)>();
        var total = 0m;
        var totalCost = 0m;
        var pieceCount = 0;
        var packageCount = int.MaxValue;
        var allStockUsed = true;

        for (var i = 0; i < commodities.Count; i++)
        {
            if (counts[i] < commodities[i].AvailableCount)
            {
                allStockUsed = false;
            }

            if (counts[i] <= 0)
            {
                continue;
            }

            lines.Add((commodities[i].Id, counts[i]));
            total += counts[i] * commodities[i].PieceWeight;
            totalCost += counts[i] * commodities[i].UnitCost;
            pieceCount += counts[i];
            packageCount = Math.Min(packageCount, commodities[i].AvailableCount / counts[i]);
        }

        var success = Math.Abs(total - requestedAmount) <= tolerance;
        if (success)
        {
            return new SubstitutionCombination(
                lines, total, true, null, totalCost, pieceCount,
                packageCount == int.MaxValue ? 0 : packageCount, Rank: null);
        }

        // Başarısız: teknik neden — tüm stok tükendiyse "StockExhausted", değilse kalan fark.
        var failureReason = allStockUsed
            ? SubstitutionReasonCodes.StockExhausted
            : SubstitutionReasonCodes.RemainderPrefix + (requestedAmount - total).ToString(CultureInfo.InvariantCulture);

        return new SubstitutionCombination(
            lines, total, false, failureReason, totalCost, pieceCount, PackageCount: 0, Rank: null);
    }

    /// <summary>Skor (konsept madde 1, lexicographic): TotalCost KÜÇÜK → PieceCount KÜÇÜK → PackageCount
    /// BÜYÜK; eşitlikte numaralandırma sırası korunur (kararlı sıralama). Rank 1 = ana varyant adayı.</summary>
    private static void AssignRanks(List<SubstitutionCombination> all)
    {
        var successIndexes = new List<int>();
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Success)
            {
                successIndexes.Add(i);
            }
        }

        var ordered = successIndexes
            .OrderBy(i => all[i].TotalCost)
            .ThenBy(i => all[i].PieceCount)
            .ThenByDescending(i => all[i].PackageCount)
            .ToList();

        for (var rank = 0; rank < ordered.Count; rank++)
        {
            var index = ordered[rank];
            all[index] = all[index] with { Rank = rank + 1 };
        }
    }
}
