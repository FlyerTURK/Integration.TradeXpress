using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// STOK (hurda) kategorisi — firmanın fiziksel HURDA-MADEN holding'i (<see cref="IScrapReportAppService"/> ana leg:
/// Total=HAS @ MainUnit, tüm ödeme tipleri/Peşin dahil, asOf gün-sonu net). ERPPRO'da hurda = IsHurda bayraklı MADEN →
/// STOK-Has bacağına girer (Stone/emtia DEĞİL). Cari hurda (BAKIYE, ledger'daki karşı-taraf) AYRI boyut → OFFSET,
/// çift sayım DEĞİL (Metal ile aynı; ERPPRO BAKİYE+STOK paritesi). İŞARET: +Net AS-IS (fiziksel holding firma-perspektifi;
/// −Σ UYGULANMAZ). Değerleme + TOPLAM merkezde (val.Buy ile base'e re-base; HAS kendi kuruyla). Hurda'da işçilik bacağı YOK.
/// </summary>
[ExposeServices(typeof(IBalanceSheetCategorySource))]
public class ScrapStockCategorySource : IBalanceSheetCategorySource, ITransientDependency
{
    private readonly IScrapReportAppService _scrap;

    public ScrapStockCategorySource(IScrapReportAppService scrap) => _scrap = scrap;

    public int Order => 12;

    public async Task<IReadOnlyList<BalanceSheetContribution>> GetAsync(Guid companyId, Guid? branchId, DateTime asOf)
    {
        var cutoff = asOf.Date.AddDays(1);   // gün-sonu dahil (AccountBalance/Cash/Metal ile aynı)
        var net = await _scrap.GetScrapNetByUnitAsync(branchId, vaultId: null, asOfExclusive: cutoff);

        return net
            .Where(kv => kv.Value != 0m)
            .Select(kv => new BalanceSheetContribution(BalanceSheetCategory.Stock, kv.Key, kv.Value))
            .ToList();
    }
}
