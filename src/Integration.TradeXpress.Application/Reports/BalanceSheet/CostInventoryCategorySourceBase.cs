using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// MALİYET-envanteri kategorileri (Taş/Mücevher) için ortak çekirdek. ERPPRO <c>Stok.GetTasStoklari</c> /
/// <c>Stok.Pirlanta.Maliyet</c> paritesi: voucher satırlarından CommodityId bazında ON-HAND (Giriş+/Çıkış−) toplanır,
/// her commodity'nin GİRİŞ (maliyet) fiyatıyla değerlenir = (PriceByQuantity ? Adet : Miktar) × EntryPrice @ EntryPriceUnit.
/// Para-birimi maliyet katkısı üretir; HAS/milyem YOK (maden değil). Değerleme/re-base + TOPLAM merkezde
/// (<c>BalanceSheetReportAppService</c> EntryPriceUnit'i base'e çevirir). İŞARET: +Net (fiziksel holding firma-perspektifi;
/// −Σ UYGULANMAZ). Cari parasal bacak (PayTotal→AccountBalance) AYRI boyut → satışta maliyet(stok−)+satış-fiyatı(cari+)=marj kârı.
/// </summary>
public abstract class CostInventoryCategorySourceBase : IBalanceSheetCategorySource, IBalanceSheetCommodityDrill
{
    private readonly IRepository<Voucher, Guid> _vouchers;
    protected readonly IAsyncQueryableExecuter Executer;

    protected CostInventoryCategorySourceBase(IRepository<Voucher, Guid> vouchers, IAsyncQueryableExecuter executer)
    {
        _vouchers = vouchers;
        Executer  = executer;
    }

    public abstract int Order { get; }
    protected abstract ProcessType ProcessKind { get; }
    protected abstract string Category { get; }

    /// <summary>CommodityId → maliyet bilgisi (giriş fiyatı, fiyat birimi, adet-mi-miktar-mı). Host katalog dahil.</summary>
    protected abstract Task<Dictionary<Guid, CommodityCost>> LoadCostsAsync(IReadOnlyCollection<Guid> commodityIds);

    public async Task<IReadOnlyList<BalanceSheetContribution>> GetAsync(Guid companyId, Guid? branchId, DateTime asOf)
    {
        var cutoff = asOf.Date.AddDays(1);   // gün-sonu dahil
        var pt = ProcessKind;

        // K4: CommodityId bazında on-hand (işaret: Giriş + / Çıkış −) SQL-side GROUP BY + koşullu SUM
        // (ledger deseni) — satırlar belleğe çekilmez; maliyet lookup'ı zaten Id listesiyle çalışır.
        var vq = await _vouchers.GetQueryableAsync();
        var onHand = await Executer.ToListAsync(
            from v in vq
            where v.CompanyId == companyId
               && (branchId == null || v.BranchId == branchId)
               && v.VoucherDate < cutoff
            from l in v.Lines
            where !l.IsDeleted && l.Type == pt && l.CommodityId != null
            group l by l.CommodityId into g
            select new
            {
                CommodityId = g.Key!.Value,
                NetQty = g.Sum(x => ((int)x.Direction % 2) == 0 ? x.Quantity : -x.Quantity),
                NetAmt = g.Sum(x => ((int)x.Direction % 2) == 0 ? x.Amount : -x.Amount),
            });

        if (onHand.Count == 0)
            return new List<BalanceSheetContribution>();

        var costs = await LoadCostsAsync(onHand.Select(o => o.CommodityId).ToList());

        // Maliyet birimine göre topla: değer = (adet|miktar) × EntryPrice.
        var byUnit = new Dictionary<Guid, decimal>();
        foreach (var o in onHand)
        {
            if (!costs.TryGetValue(o.CommodityId, out var c) || c.UnitId is not { } unitId)
                continue;
            var basis = c.ByQuantity ? o.NetQty : o.NetAmt;
            var value = basis * c.EntryPrice;
            if (value != 0m)
                byUnit[unitId] = byUnit.GetValueOrDefault(unitId) + value;
        }

        return byUnit
            .Where(kv => kv.Value != 0m)
            .Select(kv => new BalanceSheetContribution(Category, kv.Key, kv.Value))
            .ToList();
    }

    // ── DRILL (bilanço popup): tek birim için COMMODITY (taş/mücevher kodu) bazında maliyet kırılımı ──
    public string DrillCategory => Category;

    /// <summary>DRILL — GetAsync'in tek-birim COMMODITY (kod) kırılımı: on-hand × EntryPrice, kod bazında. +Net (firma-perspektifi).</summary>
    public async Task<Dictionary<string, decimal>> GetCommodityBreakdownAsync(Guid companyId, Guid? branchId, DateTime asOf, Guid unitId)
    {
        var cutoff = asOf.Date.AddDays(1);
        var pt = ProcessKind;

        var vq = await _vouchers.GetQueryableAsync();
        var lines = await Executer.ToListAsync(
            from v in vq
            where v.CompanyId == companyId
               && (branchId == null || v.BranchId == branchId)
               && v.VoucherDate < cutoff
            from l in v.Lines
            where !l.IsDeleted && l.Type == pt && l.CommodityId != null
            select new { CommodityId = l.CommodityId!.Value, l.CommodityCode, l.Direction, l.Quantity, l.Amount });

        if (lines.Count == 0)
            return new Dictionary<string, decimal>();

        var onHand = lines
            .GroupBy(x => new { x.CommodityId, x.CommodityCode })
            .Select(g => new
            {
                g.Key.CommodityId,
                Code   = g.Key.CommodityCode ?? "?",
                NetQty = g.Sum(x => Sign(x.Direction) * x.Quantity),
                NetAmt = g.Sum(x => Sign(x.Direction) * x.Amount),
            })
            .ToList();

        var costs = await LoadCostsAsync(onHand.Select(o => o.CommodityId).ToList());

        var result = new Dictionary<string, decimal>();
        foreach (var o in onHand)
        {
            if (!costs.TryGetValue(o.CommodityId, out var c) || c.UnitId is not { } uId || uId != unitId)
                continue;
            var basis = c.ByQuantity ? o.NetQty : o.NetAmt;
            var value = basis * c.EntryPrice;
            if (value != 0m)
                result[o.Code] = result.GetValueOrDefault(o.Code) + value;
        }
        return result;
    }

    private static decimal Sign(ProcessDirectionType d) => ((int)d % 2) == 0 ? 1m : -1m;
}

/// <summary>DRILL arayüzü — bir kategorinin tek birim COMMODITY(kod) bazında kırılımı (bilanço popup; Stone/Jewelry maliyet-envanteri).</summary>
public interface IBalanceSheetCommodityDrill
{
    string DrillCategory { get; }
    Task<Dictionary<string, decimal>> GetCommodityBreakdownAsync(Guid companyId, Guid? branchId, DateTime asOf, Guid unitId);
}

/// <summary>Bir commodity'nin maliyet bilgisi: giriş/maliyet fiyatı + para birimi + fiyat-adet-başına mı.</summary>
public readonly record struct CommodityCost(decimal EntryPrice, Guid? UnitId, bool ByQuantity);
