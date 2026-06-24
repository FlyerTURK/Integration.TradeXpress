using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parite çözümleme — iki birimin parite çiftleri grafiğinde <b>bağlı</b> olup olmadığını
/// (doğrudan ya da ara birimlerden <b>zincirleyerek</b>, varsayılan ≤3 seviye) bulur ve
/// bağlıysa kanonik <b>base</b>'i döndürür. Base = önceliği yüksek birim
/// (<see cref="CurrencyUnitPriority"/>) — Parity kayıtları da base'i öncelikle kurar,
/// dolayısıyla yön sıra-bağımsızdır (SAR↔TRY ve TRY↔SAR aynı base'i verir).
///
/// <para>Çiftler <c>(Base, Quote)</c> verilir ama bağlantı için <b>çift yönlü</b> (graf
/// kenarı) sayılır: ör. SARUSD (USD-SAR) + USDTRY (USD-TRY) → SAR ile TRY, USD hub'ı
/// üzerinden bağlıdır; base = öncelikli olan (SAR).</para>
/// </summary>
public static class ParityResolver
{
    /// <param name="rankOf">Birim → öncelik sırası (küçük = güçlü = base). Bilinmeyen büyük değer.</param>
    public static Guid? ResolveBaseId(
        IReadOnlyCollection<(Guid Base, Guid Quote)> pairs,
        Guid a, Guid b, Func<Guid, int> rankOf, int maxLevels = 3)
    {
        if (pairs == null || rankOf == null || a == b || a == Guid.Empty || b == Guid.Empty)
            return null;

        if (!AreConnected(pairs, a, b, maxLevels))
            return null;

        // Bağlı → base önceliğe göre (sıra-bağımsız: TRYSAR de SARTRY de aynı base).
        return rankOf(a) <= rankOf(b) ? a : b;
    }

    // Çift yönlü (undirected) BFS: a'dan b'ye en çok maxLevels kenarla ulaşılabiliyor mu.
    private static bool AreConnected(
        IReadOnlyCollection<(Guid Base, Guid Quote)> pairs, Guid a, Guid b, int maxLevels)
    {
        var visited = new HashSet<Guid> { a };
        var frontier = new List<Guid> { a };

        for (var level = 0; level < maxLevels && frontier.Count > 0; level++)
        {
            var next = new List<Guid>();
            foreach (var u in frontier)
            {
                foreach (var p in pairs)
                {
                    Guid? other = p.Base == u ? p.Quote : (p.Quote == u ? p.Base : (Guid?)null);
                    if (other is not { } o || visited.Contains(o))
                        continue;
                    if (o == b)
                        return true;
                    visited.Add(o);
                    next.Add(o);
                }
            }
            frontier = next;
        }
        return false;
    }
}
