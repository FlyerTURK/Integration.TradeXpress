using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Vouchers.Balance;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// Expense/Income (ERPPRO GIDER/GELIR) kategorileri — <see cref="ProcessType.Service"/> ledger kayıtlarından YÖNE göre
/// türetilir (kullanıcı kararı): <b>Giriş(Inbound) → Expense</b>, <b>Çıkış(Outbound) → Income</b>. Service yapısı
/// (poster/process) DEĞİŞTİRİLMEZ — yalnız okunur. P&L bilgi amaçlı: <see cref="BalanceSheetCategory.CountsInTotal"/>
/// gider/geliri TOPLAM dışı tutar → AccountBalance'taki gerçek bakiye etkisiyle ÇİFT SAYILMAZ.
/// </summary>
[ExposeServices(typeof(IBalanceSheetCategorySource))]
public class ServicePLCategorySource : IBalanceSheetCategorySource, ITransientDependency
{
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledger;
    private readonly IAsyncQueryableExecuter _executer;

    public ServicePLCategorySource(IRepository<BalanceLedgerEntry, Guid> ledger, IAsyncQueryableExecuter executer)
    {
        _ledger = ledger;
        _executer = executer;
    }

    public int Order => 50;

    public async Task<IReadOnlyList<BalanceSheetContribution>> GetAsync(Guid companyId, Guid? branchId, DateTime asOf)
    {
        // GÜN-SONU sınırı: asOf'un TÜM günü dahil (VoucherDate saat taşır → '<= gece-yarısı' o günü düşürürdü).
        var cutoff = asOf.Date.AddDays(1);
        var q = await _ledger.GetQueryableAsync();
        var raw = await _executer.ToListAsync(
            from e in q
            where e.CompanyId == companyId
               && (branchId == null || e.BranchId == branchId)
               && e.VoucherDate < cutoff
               && e.ProcessType == ProcessType.Service
               && (e.Direction == ProcessDirectionType.Inbound || e.Direction == ProcessDirectionType.Outbound)
            group e by new { e.UnitId, e.Direction } into g
            select new { g.Key.UnitId, g.Key.Direction, Amount = g.Sum(x => x.Amount) });

        // İŞARET — FİRMA perspektifi (AccountBalance ile aynı): müşteri borcu = bizim alacağımız (+). Net varlık = −Σ.
        // Çıkış(=Gelir, müşteri borçlandı, ledger −) → −(−)=+gelir; Giriş(=Gider, ledger +) → −(+)=−gider.
        return raw.Select(r => new BalanceSheetContribution(
            r.Direction == ProcessDirectionType.Inbound ? BalanceSheetCategory.Expense : BalanceSheetCategory.Income,
            r.UnitId, -r.Amount)).ToList();
    }
}
