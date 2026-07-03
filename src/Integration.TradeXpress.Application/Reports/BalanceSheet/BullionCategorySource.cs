using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Bullions;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Vouchers.Balance;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// TAKOZ (Bullion) kategorisi — firmanın fiziksel KÜLÇE holding'i. <see cref="BullionBalancePoster"/> ile AYNI motoru
/// (<see cref="BullionLegCalculator.ComputeBullion"/>) kullanır (DRY + offset simetrisi): raporlu → altın(MainUnit) +
/// gümüş(SilverUnitId) + platin(PlatinumUnitId) + paladyum(PalladiumUnitId); raporsuz → tek pseudo bacak
/// (MainUnit = <see cref="BullionConsts.PseudoUnitId"/>, ham gram). Bacaklar LegCalculator'dan ZATEN yön-işaretlidir
/// (Giriş +, Çıkış −) → poster gibi EK NEGATİFLEME UYGULANMAZ.
/// <para>ÇİFT SAYIM YOK (Metal paritesi = OFFSET, disjoint değil): poster metal bacaklarını BalanceLedgerEntry'ye yazar,
/// AccountBalanceCategorySource ledger'ı ProcessType-filtresiz süpürüp −Σ koyar → külçe içeriği BAKİYE'de negatif durur;
/// bu kaynak +içerik ekler → TOPLAM'da birbirini götürür (alış-anı break-even, ERPPRO BAKİYE+TAKOZ paritesi).</para>
/// <para>İŞÇİLİK bacağı (LaborTotal @ PayUnit) BURADA EMİT EDİLMEZ — yalnız metal içeriği. Değerleme + TOPLAM merkezde
/// (<c>BalanceSheetReportAppService</c>: gerçek birimler val.Buy ile base'e re-base; TAKOZ pseudo-birim DefaultCarpan×HAS ile).</para>
/// </summary>
[ExposeServices(typeof(IBalanceSheetCategorySource))]
public class BullionCategorySource : IBalanceSheetCategorySource, ITransientDependency
{
    private readonly IRepository<Voucher, Guid> _vouchers;
    private readonly IAsyncQueryableExecuter _executer;

    public BullionCategorySource(IRepository<Voucher, Guid> vouchers, IAsyncQueryableExecuter executer)
    {
        _vouchers = vouchers;
        _executer = executer;
    }

    public int Order => 22;   // Metal=11 / Scrap=12 / Labor=13 / Stone=20 / Jewelry=21 sonrası

    public async Task<IReadOnlyList<BalanceSheetContribution>> GetAsync(Guid companyId, Guid? branchId, DateTime asOf)
    {
        var cutoff = asOf.Date.AddDays(1);   // gün-sonu dahil (AccountBalance/Metal/Scrap ile aynı)

        // K4 NOTU: bu kaynak BİLEREK SQL-side aggregation'a İNDİRİLMEDİ — bacaklar satır-başı koşullu işaret
        // motorundan (BullionLegCalculator.ComputeBullion: IsReport/Mode dallanmaları + milyem çarpımları) türetilir;
        // SQL'e çevrilemez, zorlanırsa client-eval/yanlış sonuç riski. Projeksiyon zaten dar (entity çekilmez).
        // Takoz satırlarının ham alanları + kayıt anı kur snapshot'ları — poster'ın okuduğu alanların aynısı.
        var vq = await _vouchers.GetQueryableAsync();
        var lines = await _executer.ToListAsync(
            from v in vq
            where v.CompanyId == companyId
               && (branchId == null || v.BranchId == branchId)
               && v.VoucherDate < cutoff
            from l in v.Lines
            where !l.IsDeleted && l.Type == ProcessType.Bullion
            select new BullionLineData(
                l.Direction, l.IsReport, l.Amount, l.AssayAmount, l.Factor,
                l.SilverFactor, l.PlatinumFactor, l.PalladiumFactor,
                l.SilverMode, l.PlatinumMode, l.PalladiumMode,
                l.PayFactor, l.SilverLaborRate, l.PlatinumLaborRate, l.PalladiumLaborRate,
                l.GoldRate, l.SilverRate, l.PlatinumRate, l.PalladiumRate,
                l.PayUnitRate, l.GoldLaborUnitRate, l.SilverLaborUnitRate, l.PlatinumLaborUnitRate, l.PalladiumLaborUnitRate,
                l.MainUnitId, l.SilverUnitId, l.PlatinumUnitId, l.PalladiumUnitId));

        if (lines.Count == 0)
        {
            return new List<BalanceSheetContribution>();
        }

        // Birim bazında metal içeriği (poster ile AYNI motor + AYNI bacak→birim eşlemesi; işçilik HARİÇ).
        var byUnit = new Dictionary<Guid, decimal>();
        foreach (var l in lines)
        {
            var legs = BullionLegCalculator.ComputeBullion(new BullionLegInput(
                Direction:              l.Direction,
                IsReport:               l.IsReport ?? false,
                Amount:                 l.Amount,
                AssayAmount:            l.AssayAmount ?? 0m,
                GoldFactor:             l.Factor,
                SilverFactor:           l.SilverFactor ?? 0m,
                PlatinumFactor:         l.PlatinumFactor ?? 0m,
                PalladiumFactor:        l.PalladiumFactor ?? 0m,
                SilverMode:             l.SilverMode,
                PlatinumMode:           l.PlatinumMode,
                PalladiumMode:          l.PalladiumMode,
                GoldLaborRate:          l.PayFactor,
                SilverLaborRate:        l.SilverLaborRate ?? 0m,
                PlatinumLaborRate:      l.PlatinumLaborRate ?? 0m,
                PalladiumLaborRate:     l.PalladiumLaborRate ?? 0m,
                GoldRate:               l.GoldRate ?? 0m,
                SilverRate:             l.SilverRate ?? 0m,
                PlatinumRate:           l.PlatinumRate ?? 0m,
                PalladiumRate:          l.PalladiumRate ?? 0m,
                PayUnitRate:            l.PayUnitRate,
                GoldLaborUnitRate:      l.GoldLaborUnitRate ?? 0m,
                SilverLaborUnitRate:    l.SilverLaborUnitRate ?? 0m,
                PlatinumLaborUnitRate:  l.PlatinumLaborUnitRate ?? 0m,
                PalladiumLaborUnitRate: l.PalladiumLaborUnitRate ?? 0m));

            // Bacak → birim eşlemesi poster ile BİREBİR (offset garanti); işçilik (LaborTotal) atlanır.
            // Raporsuz → MainUnit zaten PseudoUnitId (panel'de öyle set edilir) → merkez DefaultCarpan×HAS ile değerler.
            Add(byUnit, l.MainUnitId,      legs.UnreportedTotal);
            Add(byUnit, l.MainUnitId,      legs.GoldTotal);
            Add(byUnit, l.SilverUnitId,    legs.SilverTotal);
            Add(byUnit, l.PlatinumUnitId,  legs.PlatinumTotal);
            Add(byUnit, l.PalladiumUnitId, legs.PalladiumTotal);
        }

        return byUnit
            .Where(kv => kv.Value != 0m)
            .Select(kv => new BalanceSheetContribution(BalanceSheetCategory.Bullion, kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>Sıfır-olmayan yön-işaretli bacağı, geçerli bir birime (Guid.Empty değil) ekler.</summary>
    private static void Add(Dictionary<Guid, decimal> byUnit, Guid? unitId, decimal amount)
    {
        if (amount == 0m || unitId is not { } id || id == Guid.Empty)
        {
            return;
        }
        byUnit[id] = byUnit.GetValueOrDefault(id) + amount;
    }

    /// <summary>Takoz satırının motor için gereken ham alanları (LINQ projeksiyonu — entity çekmeden).</summary>
    private sealed record BullionLineData(
        ProcessDirectionType Direction, bool? IsReport, decimal Amount, decimal? AssayAmount, decimal Factor,
        decimal? SilverFactor, decimal? PlatinumFactor, decimal? PalladiumFactor,
        MetalDisposition? SilverMode, MetalDisposition? PlatinumMode, MetalDisposition? PalladiumMode,
        decimal PayFactor, decimal? SilverLaborRate, decimal? PlatinumLaborRate, decimal? PalladiumLaborRate,
        decimal? GoldRate, decimal? SilverRate, decimal? PlatinumRate, decimal? PalladiumRate,
        decimal PayUnitRate, decimal? GoldLaborUnitRate, decimal? SilverLaborUnitRate, decimal? PlatinumLaborUnitRate, decimal? PalladiumLaborUnitRate,
        Guid MainUnitId, Guid? SilverUnitId, Guid? PlatinumUnitId, Guid? PalladiumUnitId);
}
