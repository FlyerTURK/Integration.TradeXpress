using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// STOK (nakit) kategorisi — firmanın fiziksel NAKİT holding'i (<see cref="ICashReportAppService"/> iki-leg nakit
/// çıkarımından, asOf gün-sonu net). Cari ledger'dan AYRI: CashBalancePoster fiziksel nakdi ledger'a YAZMAZ
/// (Peşin/WithCash → yield break, yalnız karşı-taraf cari bacağını yazar) → AccountBalance ile DISJOINT, çift sayım YOK.
/// İŞARET: +Net AS-IS — fiziksel holding firma-perspektifidir (+); AccountBalance/ServicePL'deki −Σ (hesap-perspektifi)
/// kuralı BURADA UYGULANMAZ (negatiflersek nakit varlığı borca dönerdi). Değerleme + TOPLAM merkezde
/// (<c>BalanceSheetReportAppService</c> val.Buy ile base'e re-base).
/// </summary>
[ExposeServices(typeof(IBalanceSheetCategorySource))]
public class CashStockCategorySource : IBalanceSheetCategorySource, ITransientDependency
{
    private readonly ICashReportAppService _cash;

    public CashStockCategorySource(ICashReportAppService cash) => _cash = cash;

    public int Order => 10;

    public async Task<IReadOnlyList<BalanceSheetContribution>> GetAsync(Guid companyId, Guid? branchId, DateTime asOf)
    {
        // Gün-sonu dahil (AccountBalance/ServicePL ile aynı): VoucherDate < asOf+1. companyId QueryCashLegsAsync içinde
        // ICurrentCompany'den zorlanır — bilanço servisi de aynı şirketi zorladığından tutarlı (companyId == ICurrentCompany.Id).
        var cutoff = asOf.Date.AddDays(1);
        var net = await _cash.GetCashNetByUnitAsync(branchId, vaultId: null, asOfExclusive: cutoff);

        return net
            .Where(kv => kv.Value != 0m)
            .Select(kv => new BalanceSheetContribution(BalanceSheetCategory.Stock, kv.Key, kv.Value))
            .ToList();
    }
}
