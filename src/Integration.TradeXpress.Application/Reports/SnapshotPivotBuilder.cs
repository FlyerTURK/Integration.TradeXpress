using System;
using System.Collections.Generic;
using System.Linq;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Kayıtlı bilanço snapshot satırlarından GEÇMİŞ listesi pivot'unu üreten SAF hesap çekirdeği (veri erişimi yok;
/// <see cref="BalanceSheetReportAppService.GetSnapshotListAsync"/> sorgular, bu sınıf hesaplar):
/// AsOfDate bazında PIVOT (kategori→Net) → TOPLAM (CountsInTotal kategoriler) → tarih ARTAN running türetim
/// (DEVIR=önceki TOPLAM · KARZARAR=TOPLAM−DEVIR · MASRAF=Expense+Income · GUNLUK=MASRAF delta) +
/// KURFARKI (ERPPRO <c>GetKurFarki</c> paritesi: ardışık snapshot çifti, birim bazlı yeniden değerleme).
/// </summary>
internal static class SnapshotPivotBuilder
{
    /// <summary>Saf-hesap girdi satırı — AppService, EF anonim projeksiyonunu buna map'ler (sorgu şekli değişmez).</summary>
    internal sealed record SnapshotRow(
        DateTime AsOfDate,
        Guid? BranchId,
        string Category,
        Guid UnitId,
        decimal Amount,
        decimal ValuationRate,
        decimal Net,
        string? BaseCurrencyCode);

    /// <summary>Bir snapshot gününün pivot satırı + KURFARKI için birim-bazlı yeniden değerleme detayı.</summary>
    private sealed record DayPivot(BalanceSheetSnapshotRowDto Row, Dictionary<Guid, SnapshotUnitCell> UnitDetail);

    /// <summary>Bir gün+birim için yeniden değerleme hücresi: donuk Amount + donuk ValuationRate + donuk Net.</summary>
    private readonly record struct SnapshotUnitCell(decimal Amount, decimal ValuationRate, decimal Net);

    /// <summary>Pivot + running türetimleri üretir; satırlar tarih ARTAN sıralı döner.</summary>
    public static List<BalanceSheetSnapshotRowDto> Build(
        IReadOnlyCollection<SnapshotRow> rows,
        BalanceSheetScope scope,
        IReadOnlyDictionary<Guid, string> branchCodes)
    {
        // AsOfDate bazında PIVOT (tarih ARTAN). Birim-detayı (KURFARKI için) günle beraber tutulur.
        var days = rows
            .GroupBy(r => r.AsOfDate)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var categoryNets = g.GroupBy(x => x.Category)
                    .ToDictionary(cg => cg.Key, cg => cg.Sum(x => x.Net));
                var total  = categoryNets.Where(kv => BalanceSheetCategory.CountsInTotal(kv.Key)).Sum(kv => kv.Value);
                var masraf = categoryNets.GetValueOrDefault(BalanceSheetCategory.Expense)
                           + categoryNets.GetValueOrDefault(BalanceSheetCategory.Income);
                var firstBranchId = g.Select(x => x.BranchId).FirstOrDefault(id => id != null);

                // KURFARKI için birim-detay: Expense/Income (P&L) + MissingRate (donuk rate=0) satırları HARİÇ.
                // Aynı gün + aynı UnitId'nin birden çok kategori satırı olabilir → UnitId bazında Amount/Net topla,
                // ValuationRate birim-başına sabit (aynı asOf kuru) → ilkini al.
                var unitDetail = g
                    .Where(x => x.ValuationRate != 0m
                             && x.Category != BalanceSheetCategory.Expense
                             && x.Category != BalanceSheetCategory.Income)
                    .GroupBy(x => x.UnitId)
                    .ToDictionary(
                        ug => ug.Key,
                        ug => new SnapshotUnitCell(ug.Sum(x => x.Amount), ug.First().ValuationRate, ug.Sum(x => x.Net)));

                return new DayPivot(
                    new BalanceSheetSnapshotRowDto
                    {
                        AsOfDate         = g.Key,
                        Scope            = scope,
                        BranchCode       = firstBranchId is { } fb ? branchCodes.GetValueOrDefault(fb) : null,
                        CategoryNets     = categoryNets,
                        Total            = total,
                        Masraf           = masraf,
                        BaseCurrencyCode = g.Select(x => x.BaseCurrencyCode).FirstOrDefault() ?? string.Empty,
                    },
                    unitDetail);
            })
            .ToList();

        // Running türetimler (tarih sırasına göre akümülatör) + KURFARKI (ardışık gün çifti).
        decimal prevTotal = 0m;
        decimal? prevMasraf = null;
        for (var i = 0; i < days.Count; i++)
        {
            var row = days[i].Row;
            row.Devir    = prevTotal;                        // önceki günün TOPLAM'ı (ilk gün 0)
            row.KarZarar = row.Total - row.Devir;            // dönemler arası net varlık değişimi
            row.Gunluk   = prevMasraf is { } pm ? row.Masraf - pm : row.Masraf;   // MASRAF delta (ilk gün MASRAF'ın kendisi)

            // KURFARKI: önceki snapshot satırının pozisyonunu BU günün kuruyla yeniden değerle → o günün Fark'ını
            // İLİŞTİR (ERPPRO T-1: dünkü pozisyonun bugünkü kurla yeniden değerlemesi bu satırda görünür). İlk gün = 0.
            if (i > 0)
            {
                var prev = days[i - 1].UnitDetail;
                var curr = days[i].UnitDetail;
                decimal fark = 0m;
                foreach (var (unitId, prevCell) in prev)
                {
                    // Bu günde AYNI birim yoksa (pozisyon kapanmış) katkı 0 (yeniden değerlenecek rate yok).
                    if (!curr.TryGetValue(unitId, out var currCell))
                    {
                        continue;
                    }
                    fark += prevCell.Amount * currCell.ValuationRate - prevCell.Net;
                }
                row.KurFarki = fark;
            }

            prevTotal  = row.Total;
            prevMasraf = row.Masraf;
        }

        return days.Select(d => d.Row).ToList();
    }
}
