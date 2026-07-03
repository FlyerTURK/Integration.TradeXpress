using System;
using System.Collections.Generic;
using Integration.TradeXpress.Bullions;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Blazor.Client.Pages.CurrentTransactions;

/// <summary>Konsolide toplam + tamlık bayrağı (kuru olmayan birim toplama katılamadıysa false → ≈ göstergesi).</summary>
public readonly record struct ConsolidatedBalanceResult(decimal Total, bool IsComplete);

/// <summary>
/// Bakiye satırlarını pivot (TRY) Buy kurlarıyla hesabın bakiye birimine çevirip toplayan SAF hesap
/// (UI/state bağımlılığı yok — test edilebilir). Kuru olmayan birim toplama katılmaz, sonuç ≈ (eksik) işaretlenir.
/// TAKOZ pseudo-birimin kuru yok — legacy <c>FN.TakozKur</c> paritesi: DefaultCarpan(0.6) × HAS kuru
/// (Report.BakiyeListesi: <c>TAKOZ × 0.600 × KurHas</c>; 1000 TAKOZ → "600.00 HAS (A)").
/// </summary>
public static class ConsolidatedBalanceCalculator
{
    /// <summary>Verilen bakiye satırlarını <paramref name="baseUnitId"/> cinsine çevirip toplar.</summary>
    /// <param name="rows">Görünen bakiye satırları.</param>
    /// <param name="baseUnitId">Hesabın bakiye (base) birimi.</param>
    /// <param name="pivotBuyByUnitId">Birim → pivot (TRY) Buy kuru — tutarlı tek-yön.</param>
    /// <param name="hasBuy">HAS biriminin pivot Buy kuru (TAKOZ pseudo-birim değerlemesi için).</param>
    public static ConsolidatedBalanceResult Calculate(
        IReadOnlyList<VoucherBalanceLineDto> rows,
        Guid baseUnitId,
        IReadOnlyDictionary<Guid, decimal> pivotBuyByUnitId,
        decimal hasBuy)
    {
        decimal total = 0m;
        var complete = true;
        var baseBuy = pivotBuyByUnitId.GetValueOrDefault(baseUnitId);

        foreach (var row in rows)
        {
            if (row.Net == 0m)
            {
                continue;
            }

            if (row.UnitId == baseUnitId)
            {
                total += row.Net;                     // zaten base cinsinden
                continue;
            }

            // TAKOZ pseudo-birim: kur tablosunda yok → Carpan × HAS kuru ile değerle.
            var unitBuy = row.UnitId == BullionConsts.PseudoUnitId
                ? BullionConsts.DefaultCarpan * hasBuy
                : pivotBuyByUnitId.GetValueOrDefault(row.UnitId);
            if (unitBuy <= 0m || baseBuy <= 0m)
            {
                complete = false;                     // değerlenemedi
                continue;
            }

            total += row.Net * unitBuy / baseBuy;     // pivot üzerinden base'e çevir
        }

        return new ConsolidatedBalanceResult(total, complete);
    }
}
