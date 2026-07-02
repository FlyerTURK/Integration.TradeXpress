using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Vouchers.Balance;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// AccountBalance (ERPPRO BAKIYE) kategorisi — kalıcı <see cref="BalanceLedgerEntry"/>'i kapsam+tarihle
/// GROUP BY UnitId + SUM ile birim-net'e indirir (pozisyon raporuyla AYNI sorgu; pozisyon = bunun değer-filtreli
/// alt-kümesi). VoucherDate &lt;= asOf → o güne kadarki bakiye. Kural HARDCODE değil — net tamamen ledger'dan.
/// </summary>
[ExposeServices(typeof(IBalanceSheetCategorySource))]
public class AccountBalanceCategorySource : IBalanceSheetCategorySource, ITransientDependency
{
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledger;
    private readonly IAsyncQueryableExecuter _executer;

    public AccountBalanceCategorySource(IRepository<BalanceLedgerEntry, Guid> ledger, IAsyncQueryableExecuter executer)
    {
        _ledger = ledger;
        _executer = executer;
    }

    public int Order => 0;

    public async Task<IReadOnlyList<BalanceSheetContribution>> GetAsync(Guid companyId, Guid? branchId, DateTime asOf)
    {
        // GÜN-SONU sınırı: asOf'un TÜM günü dahil (VoucherDate saat bileşeni taşır → '<= gece-yarısı' o günü düşürürdü).
        var cutoff = asOf.Date.AddDays(1);
        var q = await _ledger.GetQueryableAsync();
        var raw = await _executer.ToListAsync(
            from e in q
            where e.CompanyId == companyId
               && (branchId == null || e.BranchId == branchId)
               && e.VoucherDate < cutoff
            group e by e.UnitId into g
            select new { UnitId = g.Key, Amount = g.Sum(x => x.Amount) });

        // İŞARET — FİRMA perspektifi: ledger HESAP bakiyesini saklar (müşteri borçlanır → −). Bilanço firmanın NET
        // VARLIĞINI gösterir → müşteri borcu = bizim ALACAĞIMIZ/varlığımız (+), müşteri alacağı = bizim borcumuz (−).
        // Net varlık = −Σ(hesap bakiyesi). (Kullanıcı: "hizmet çıkış = müşteri borçlanır"; alacaklı = kârda = +.)
        // NOT: fiziksel stok kategorileri (Faz 2) NEGATİFLENMEZ — holding zaten + varlıktır.
        return raw.Select(r => new BalanceSheetContribution(BalanceSheetCategory.AccountBalance, r.UnitId, -r.Amount)).ToList();
    }
}
