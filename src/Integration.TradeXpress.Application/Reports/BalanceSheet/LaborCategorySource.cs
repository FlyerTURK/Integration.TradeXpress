using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// İŞÇİLİK (Labor) kategorisi — envanterdeki işçilik <b>SERMAYESİ/VARLIĞI</b> (kuyumculukta işçilik ürün maliyetinin parçası,
/// vitrindeki varlık). <see cref="IMetalReportAppService.GetMetalLaborByUnitAsync"/>: maden Normal/İade/Emanet işçilik bacağı
/// (PayUnit/PayTotal) net — GİRİŞ(alış)+, ÇIKIŞ(satış)−, merkez base'e (ör. HAS) çevirir. <b>TOPLAM'a GİRER</b> ve BAKİYE'deki
/// (AccountBalance) işçilik cari'sini OFFSET eder (ERPPRO paritesi: alış-anı BAKİYE −36.13 + İŞÇİLİK +3.08 + STOK +33.05 = 0 break-even).
/// Kaynak = VoucherLine PayTotal (başka yerde işçilik kaydı yok — kullanıcı). ⚠ ÇIKIŞ şu an satış PayTotal'ı ile düşüyor (COST değil) →
/// alış+satış marj kârı için maliyet-takibi (ERPPRO GetMadenMaliyeti) gerek; gelecek faz.
/// </summary>
[ExposeServices(typeof(IBalanceSheetCategorySource))]
public class LaborCategorySource : IBalanceSheetCategorySource, ITransientDependency
{
    private readonly IMetalReportAppService _metal;

    public LaborCategorySource(IMetalReportAppService metal) => _metal = metal;

    public int Order => 13;

    public async Task<IReadOnlyList<BalanceSheetContribution>> GetAsync(Guid companyId, Guid? branchId, DateTime asOf)
    {
        var cutoff = asOf.Date.AddDays(1);   // gün-sonu dahil
        var net = await _metal.GetMetalLaborByUnitAsync(branchId, vaultId: null, asOfExclusive: cutoff);

        return net
            .Where(kv => kv.Value != 0m)
            .Select(kv => new BalanceSheetContribution(BalanceSheetCategory.Labor, kv.Key, kv.Value))
            .ToList();
    }
}
