using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// STOK (maden) kategorisi — firmanın fiziksel MADEN holding'i (<see cref="IMetalReportAppService"/> voucher-line
/// çıkarımından: Amount @ MainUnit, tüm ödeme tipleri/Peşin dahil, asOf gün-sonu net). ERPPRO STOK-maden
/// (GetMadenStoklari) karşılığı. Cari metal (BAKIYE/AccountBalance, ledger'daki karşı-taraf metal cari'si) AYRI
/// boyuttur → ERPPRO BAKİYE+STOK paritesi gibi OFFSET, çift sayım DEĞİL (ikisi de TOPLAM'da).
/// İŞARET: +Net AS-IS (fiziksel holding firma-perspektifi +; −Σ UYGULANMAZ). Değerleme + TOPLAM merkezde
/// (<c>BalanceSheetReportAppService</c> val.Buy ile base'e re-base; maden birimi kendi kuruyla).
/// İŞÇİLİK (ERPPRO ISCILIK = eldeki-stok işçilik maliyeti) yeni projede karşılığı yok → bu fazda üretilmez.
/// </summary>
[ExposeServices(typeof(IBalanceSheetCategorySource))]
public class MetalStockCategorySource : IBalanceSheetCategorySource, ITransientDependency
{
    private readonly IMetalReportAppService _metal;

    public MetalStockCategorySource(IMetalReportAppService metal) => _metal = metal;

    public int Order => 11;

    public async Task<IReadOnlyList<BalanceSheetContribution>> GetAsync(Guid companyId, Guid? branchId, DateTime asOf)
    {
        var cutoff = asOf.Date.AddDays(1);   // gün-sonu dahil (AccountBalance/Cash ile aynı)
        var net = await _metal.GetMetalNetByUnitAsync(branchId, vaultId: null, asOfExclusive: cutoff);

        return net
            .Where(kv => kv.Value != 0m)
            .Select(kv => new BalanceSheetContribution(BalanceSheetCategory.Stock, kv.Key, kv.Value))
            .ToList();
    }
}
