using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Branches;
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
/// <para>DÖNEM KESME (ERPPRO <c>Tarih &gt; Sube.RevCostDate</c> paritesi): her kayıt KENDİ şubesinin
/// <see cref="Branch.ProfitResetDate"/>'inden SONRAKİ (strict) ise cari döneme sayılır. Company-konsolide'de bile
/// per-branch (şubeler farklı tarihte kapanabilir). null reset → <see cref="DateTime.MinValue"/> (hepsi dahil, mevcut
/// davranış). Kesme YALNIZ P&L'e; net-varlık kaynakları (AccountBalance/stok) gerçek kümülatiftir, DOKUNULMAZ.</para>
/// </summary>
[ExposeServices(typeof(IBalanceSheetCategorySource))]
public class ServicePLCategorySource : IBalanceSheetCategorySource, ITransientDependency
{
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledger;
    private readonly IRepository<Branch, Guid> _branches;
    private readonly IAsyncQueryableExecuter _executer;

    public ServicePLCategorySource(
        IRepository<BalanceLedgerEntry, Guid> ledger,
        IRepository<Branch, Guid> branches,
        IAsyncQueryableExecuter executer)
    {
        _ledger = ledger;
        _branches = branches;
        _executer = executer;
    }

    public int Order => 50;

    public async Task<IReadOnlyList<BalanceSheetContribution>> GetAsync(Guid companyId, Guid? branchId, DateTime asOf)
    {
        // GÜN-SONU sınırı: asOf'un TÜM günü dahil (VoucherDate saat taşır → '<= gece-yarısı' o günü düşürürdü).
        var cutoff = asOf.Date.AddDays(1);

        // Kapsamdaki şubelerin dönem-başlangıç (ProfitResetDate) map'i — per-branch P&L alt-sınırı (ERPPRO RevCostDate).
        // Branch scope → tek şube; Company scope (branchId null) → şirketin TÜM şubeleri (her biri farklı tarihte kapanmış olabilir).
        var bq = await _branches.GetQueryableAsync();
        var resetMap = (await _executer.ToListAsync(
                bq.Where(b => b.CompanyId == companyId && (branchId == null || b.Id == branchId))
                  .Select(b => new { b.Id, b.ProfitResetDate })))
            .ToDictionary(b => b.Id, b => b.ProfitResetDate);

        // Service ledger satırlarını çek (üst sınır DB'de; per-branch alt sınır memory'de → GROUP BY sonra).
        var q = await _ledger.GetQueryableAsync();
        var entries = await _executer.ToListAsync(
            from e in q
            where e.CompanyId == companyId
               && (branchId == null || e.BranchId == branchId)
               && e.VoucherDate < cutoff
               && e.ProcessType == ProcessType.Service
               && (e.Direction == ProcessDirectionType.Inbound || e.Direction == ProcessDirectionType.Outbound)
            select new { e.BranchId, e.VoucherDate, e.UnitId, e.Direction, e.Amount });

        // Per-branch dönem kesme: kaydın VoucherDate'i KENDİ şubesinin ProfitResetDate'inden SONRAKİ (strict) olmalı.
        // Reset null → DateTime.MinValue (hepsi dahil, mevcut davranış). Sonra UnitId+Direction bazında topla.
        var raw = entries
            .Where(e => e.VoucherDate > (resetMap.GetValueOrDefault(e.BranchId) ?? DateTime.MinValue))
            .GroupBy(e => new { e.UnitId, e.Direction })
            .Select(g => new { g.Key.UnitId, g.Key.Direction, Amount = g.Sum(x => x.Amount) });

        // İŞARET — FİRMA perspektifi (AccountBalance ile aynı): müşteri borcu = bizim alacağımız (+). Net varlık = −Σ.
        // Çıkış(=Gelir, müşteri borçlandı, ledger −) → −(−)=+gelir; Giriş(=Gider, ledger +) → −(+)=−gider.
        return raw.Select(r => new BalanceSheetContribution(
            r.Direction == ProcessDirectionType.Inbound ? BalanceSheetCategory.Expense : BalanceSheetCategory.Income,
            r.UnitId, -r.Amount)).ToList();
    }
}
