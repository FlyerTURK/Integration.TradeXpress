using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// Bir bilanço veri kaynağı — PLUGGABLE. Yeni veri (Service P&L, maden stok, taş, takoz, ERP-dışı yeni) =
/// yeni implementasyon + DI kaydı; <c>BalanceSheetReportAppService</c> hepsini otomatik toplar. Bir kaynak
/// BİRDEN ÇOK kategoriye katkı verebilir (ERPPRO OzetBilanco UNION gibi: Service → Expense+Income; maden → Stock+Labor).
/// Kaynak yalnız HAM (Category, birim, miktar) katkısı üretir; değerleme (re-base) + TOPLAM compute service'te (tek yer).
/// </summary>
public interface IBalanceSheetCategorySource
{
    /// <summary>Sunum/işlem sırası (küçük = önce).</summary>
    int Order { get; }

    /// <summary>Kapsam (companyId + branchId; branchId null = şirket konsolide) + tarih için kategori-bazlı katkılar.</summary>
    Task<IReadOnlyList<BalanceSheetContribution>> GetAsync(Guid companyId, Guid? branchId, DateTime asOf);
}

/// <summary>Bir kaynağın tek katkısı: kategori (<see cref="BalanceSheetCategory"/>) + birim + (kendi cinsinden) miktar.</summary>
public record BalanceSheetContribution(string Category, Guid UnitId, decimal Amount);
