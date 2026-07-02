using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Bullions;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Reports.BalanceSheet;
using Integration.TradeXpress.Vouchers.Balance;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Bilanço raporu (FULL net-varlık; pozisyon = exposure alt-kümesi). Tüm pluggable kategori kaynaklarını
/// (<see cref="IBalanceSheetCategorySource"/>) kapsam+tarihle toplar, base (bilanço) birimine re-base'li değerler
/// (pozisyonla AYNI <c>IEffectivePriceAppService</c>), kategori toplamı + TOPLAM (net varlık) üretir.
/// Sızıntı önleme: CompanyId DAİMA working-context'ten (<c>ICurrentCompany</c>) zorlanır.
/// </summary>
[Authorize(TradeXpressPermissions.Reports.BalanceSheet)]
public class BalanceSheetReportAppService : TradeXpressAppService, IBalanceSheetReportAppService
{
    private readonly IEnumerable<IBalanceSheetCategorySource> _sources;
    private readonly IRepository<Branch, Guid> _branches;
    private readonly IRepository<Company, Guid> _companies;
    private readonly IRepository<CurrencyUnit, Guid> _units;
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledger;
    private readonly IRepository<Account, Guid> _accounts;
    private readonly IRepository<SubAccount, Guid> _subAccounts;
    private readonly IMetalReportAppService _metalReport;
    private readonly IScrapReportAppService _scrapReport;
    private readonly ICashReportAppService _cashReport;
    private readonly IEffectivePriceAppService _pricing;
    private readonly ICurrentCompany _currentCompany;

    public BalanceSheetReportAppService(
        IEnumerable<IBalanceSheetCategorySource> sources,
        IRepository<Branch, Guid> branches,
        IRepository<Company, Guid> companies,
        IRepository<CurrencyUnit, Guid> units,
        IRepository<BalanceLedgerEntry, Guid> ledger,
        IRepository<Account, Guid> accounts,
        IRepository<SubAccount, Guid> subAccounts,
        IMetalReportAppService metalReport,
        IScrapReportAppService scrapReport,
        ICashReportAppService cashReport,
        IEffectivePriceAppService pricing,
        ICurrentCompany currentCompany)
    {
        _sources        = sources;
        _branches       = branches;
        _companies      = companies;
        _units          = units;
        _ledger         = ledger;
        _accounts       = accounts;
        _subAccounts    = subAccounts;
        _metalReport    = metalReport;
        _scrapReport    = scrapReport;
        _cashReport     = cashReport;
        _pricing        = pricing;
        _currentCompany = currentCompany;
    }

    /// <summary>
    /// DRILL — bir kategori×birim değerinin oluştuğu HAREKETLER (çift-tık popup; Kod=belge no, Bakiye=firma-perspektifi).
    /// BAKİYE(cari) = ledger entries (VoucherNumber + −Amount; BAKİYE = −Σ(ledger) olduğundan işaret görünenle aynı).
    /// Diğer kategoriler (İşçilik/Stok/Taş/Mücevher) HENÜZ desteklenmiyor → Supported=false. Kapsam ComputeAsync ile AYNI.
    /// </summary>
    public virtual async Task<BalanceSheetMovementResultDto> GetMovementsAsync(BalanceSheetMovementRequestDto input)
    {
        var result = new BalanceSheetMovementResultDto();
        if (_currentCompany.Id is not { } companyId)
            return result;

        result.UnitCode = input.UnitId == BullionConsts.PseudoUnitId
            ? CurrencyUnitCode.Bullion   // TAKOZ pseudo-birim (CurrencyUnit'te yok)
            : (await AsyncExecuter.FirstOrDefaultAsync(
                (await _units.GetQueryableAsync()).Where(u => u.Id == input.UnitId).Select(u => u.Code)) ?? string.Empty);

        var cutoff   = input.AsOf.Date.AddDays(1);
        var branchId = input.Scope == BalanceSheetScope.Company ? (Guid?)null : input.BranchId;

        if (input.Category == BalanceSheetCategory.AccountBalance)
        {
            result.Supported = true;
            var q = await _ledger.GetQueryableAsync();
            // CARİ/ALTHESAP bazında kır (belge no DEĞİL — kullanıcı cari kodu ister): (Account, SubAccount) GROUP BY + SUM.
            var grouped = await AsyncExecuter.ToListAsync(
                from e in q
                where e.CompanyId == companyId
                   && (branchId == null || e.BranchId == branchId)
                   && e.UnitId == input.UnitId
                   && e.VoucherDate < cutoff
                group e by new { e.AccountId, e.SubAccountId } into g
                select new { g.Key.AccountId, g.Key.SubAccountId, Amount = g.Sum(x => x.Amount) });

            // Kodları memory'de çöz (Account zorunlu; SubAccount opsiyonel → "cari / althesap").
            var accountIds = grouped.Select(r => r.AccountId).Distinct().ToList();
            var subIds     = grouped.Where(r => r.SubAccountId != null).Select(r => r.SubAccountId!.Value).Distinct().ToList();

            var accountCodes = (await AsyncExecuter.ToListAsync(
                    (await _accounts.GetQueryableAsync()).Where(a => accountIds.Contains(a.Id)).Select(a => new { a.Id, a.Code })))
                .ToDictionary(a => a.Id, a => a.Code);
            var subCodes = subIds.Count == 0
                ? new Dictionary<Guid, string>()
                : (await AsyncExecuter.ToListAsync(
                    (await _subAccounts.GetQueryableAsync()).Where(s => subIds.Contains(s.Id)).Select(s => new { s.Id, s.Code })))
                  .ToDictionary(s => s.Id, s => s.Code);

            result.Movements = grouped
                .Select(r => new BalanceSheetMovementDto
                {
                    Code = r.SubAccountId is { } sid && subCodes.TryGetValue(sid, out var sc)
                        ? $"{accountCodes.GetValueOrDefault(r.AccountId, "?")} / {sc}"
                        : accountCodes.GetValueOrDefault(r.AccountId, "?"),
                    Amount = -r.Amount,
                })
                .Where(m => m.Amount != 0m)
                .OrderBy(m => m.Code)
                .ToList();
        }
        else if (input.Category == BalanceSheetCategory.Labor)
        {
            // İŞÇİLİK = on-hand metal işçilik maliyeti, COMMODITY (metal kodu) bazında. +Net (varlık, NEGATİFLENMEZ).
            result.Supported = true;
            var byCode = await _metalReport.GetMetalLaborByCommodityAsync(branchId, input.UnitId, cutoff);
            result.Movements = byCode
                .Where(kv => kv.Value != 0m)
                .Select(kv => new BalanceSheetMovementDto { Code = kv.Key, Amount = kv.Value })
                .OrderBy(m => m.Code)
                .ToList();
        }
        else if (input.Category == BalanceSheetCategory.Stock)
        {
            // STOK = fiziksel maden + hurda (COMMODITY bazında) + nakit (tek "NAKİT" lump). +Net (firma-perspektifi).
            result.Supported = true;
            var merged = new Dictionary<string, decimal>();
            foreach (var kv in await _metalReport.GetMetalStockByCommodityAsync(branchId, input.UnitId, cutoff))
                merged[kv.Key] = merged.GetValueOrDefault(kv.Key) + kv.Value;
            foreach (var kv in await _scrapReport.GetScrapStockByCommodityAsync(branchId, input.UnitId, cutoff))
                merged[kv.Key] = merged.GetValueOrDefault(kv.Key) + kv.Value;
            var cash = (await _cashReport.GetCashNetByUnitAsync(branchId, vaultId: null, asOfExclusive: cutoff))
                .GetValueOrDefault(input.UnitId);
            if (cash != 0m) merged["NAKİT"] = merged.GetValueOrDefault("NAKİT") + cash;

            result.Movements = merged
                .Where(kv => kv.Value != 0m)
                .Select(kv => new BalanceSheetMovementDto { Code = kv.Key, Amount = kv.Value })
                .OrderBy(m => m.Code)
                .ToList();
        }
        else if (input.Category == BalanceSheetCategory.Stone || input.Category == BalanceSheetCategory.Jewelry)
        {
            // TAŞ/MÜCEVHER = maliyet-envanteri; ilgili kaynağın COMMODITY (taş/mücevher kodu) kırılımı. +Net.
            var drill = _sources.OfType<IBalanceSheetCommodityDrill>()
                .FirstOrDefault(d => d.DrillCategory == input.Category);
            if (drill != null)
            {
                result.Supported = true;
                var byCode = await drill.GetCommodityBreakdownAsync(companyId, branchId, input.AsOf, input.UnitId);
                result.Movements = byCode
                    .Where(kv => kv.Value != 0m)
                    .Select(kv => new BalanceSheetMovementDto { Code = kv.Key, Amount = kv.Value })
                    .OrderBy(m => m.Code)
                    .ToList();
            }
        }

        return result;
    }

    public virtual async Task<BalanceSheetReportResultDto> ComputeAsync(BalanceSheetReportFilterDto filter)
    {
        // Working şirket yoksa (host/API) boş — client CompanyId GÜVENİLMEZ, ambient'ten zorlanır.
        if (_currentCompany.Id is not { } companyId)
            return new();

        // Company scope → branchId null (konsolide). Branch scope → client şubesi (şirkete ait değilse düşür).
        Guid? branchId = filter.Scope == BalanceSheetScope.Branch ? filter.BranchId : null;
        if (branchId is { } bid)
        {
            var branch = await _branches.FindAsync(bid);
            if (branch is null || branch.CompanyId != companyId)
                branchId = null;
        }

        var baseUnitId = await ResolveBaseUnitAsync(companyId, branchId);

        // ① Tüm kaynakların kategori-bazlı katkılarını topla (sıraya göre; bir kaynak birden çok kategoriye katkı verebilir).
        var contributions = new List<(string Category, Guid UnitId, decimal Amount)>();
        foreach (var source in _sources.OrderBy(s => s.Order))
            foreach (var c in await source.GetAsync(companyId, branchId, filter.AsOf))
                if (c.Amount != 0m)
                    contributions.Add((c.Category, c.UnitId, c.Amount));

        var result = new BalanceSheetReportResultDto
        {
            Scope      = filter.Scope,
            BaseUnitId = baseUnitId,
            AsOf       = filter.AsOf,
        };

        var unitCodes = await UnitCodesAsync(contributions.Select(c => c.UnitId).Append(baseUnitId));
        result.BaseCurrencyCode = unitCodes.GetValueOrDefault(baseUnitId) ?? string.Empty;

        if (contributions.Count == 0)
            return result;

        // ② Değerleme: base'e re-base'li efektifler (pozisyonla aynı servis; base÷base=1). AsOf geçilir →
        //    geçmiş tarihli bilanço O TARİHTEKİ kurla değerlenir (bugünün kuruyla DEĞİL); bugün ise canlı/güncel.
        var valuation = (await _pricing.GetValuationByBaseAsync(baseUnitId, filter.AsOf)).ToDictionary(v => v.Id);

        // TAKOZ pseudo-birim değerlemesi: raporsuz takoz gramı, HAS'a Carpan(varsayılan milyem) ile çevrilip
        // HAS'ın base-değeriyle değerlenir (legacy BakiyeKodlari: TAKOZ→HAS × Carpan). HAS=base ise birebir.
        var hasUnitId = await HasUnitIdAsync();
        var hasRate   = hasUnitId != Guid.Empty && valuation.TryGetValue(hasUnitId, out var hv) ? hv.Buy : 0m;

        // ③ Detay satırları (kategori×birim → base'e değerlenmiş Net; ERPPRO alış kuru).
        foreach (var c in contributions)
        {
            if (c.UnitId == BullionConsts.PseudoUnitId)
            {
                var takozRate = BullionConsts.DefaultCarpan * hasRate;   // 1 TAKOZ gram = Carpan HAS × HAS-base-kuru
                result.Rows.Add(new BalanceSheetDetailRowDto
                {
                    Category      = c.Category,
                    UnitId        = c.UnitId,
                    UnitCode      = CurrencyUnitCode.Bullion,            // "TAKOZ" (CurrencyUnit'te yok — özel-durum)
                    Amount        = c.Amount,
                    ValuationRate = takozRate,
                    Net           = c.Amount * takozRate,
                    MissingRate   = hasRate == 0m,                       // HAS değerlenemezse TAKOZ da değerlenemez
                });
                continue;
            }

            valuation.TryGetValue(c.UnitId, out var val);
            result.Rows.Add(new BalanceSheetDetailRowDto
            {
                Category      = c.Category,
                UnitId        = c.UnitId,
                UnitCode      = unitCodes.GetValueOrDefault(c.UnitId),
                Amount        = c.Amount,
                ValuationRate = val?.Buy ?? 0m,
                Net           = val == null ? 0m : c.Amount * val.Buy,
                MissingRate   = val == null,
            });
        }
        result.Rows = result.Rows.OrderBy(r => r.Category).ThenBy(r => r.UnitCode).ToList();

        // ④ Kategori toplamları + ⑤ TOPLAM (net varlık = TOPLAM'a giren kategoriler).
        result.CategoryTotals = result.Rows
            .GroupBy(r => r.Category)
            .Select(g => new BalanceSheetCategoryTotalDto
            {
                Category      = g.Key,
                Net           = g.Sum(r => r.Net),
                CountsInTotal = BalanceSheetCategory.CountsInTotal(g.Key),
            })
            .OrderBy(t => t.Category)
            .ToList();

        result.Total = result.CategoryTotals.Where(t => t.CountsInTotal).Sum(t => t.Net);

        return result;
    }

    public virtual Task<BalanceSheetReportResultDto> SaveAsync(BalanceSheetReportFilterDto filter)
    {
        // TODO (Faz 1b): hesapla → BalanceSheetSnapshot tablosuna yaz (aynı scope+tarih sil+yeniden yaz).
        // Şimdilik kalıcılık YOK → compute döner (sözleşme sabit kalır, persistence sonra eklenir).
        return ComputeAsync(filter);
    }

    /// <summary>Bilanço birimi: branch base'i (boş Guid ise şirket base'ine düşer); şube yoksa şirket base'i.</summary>
    private async Task<Guid> ResolveBaseUnitAsync(Guid companyId, Guid? branchId)
    {
        if (branchId is { } bid)
        {
            var branch = await _branches.FindAsync(bid);
            if (branch != null && branch.BaseCurrencyUnitId != Guid.Empty)
                return branch.BaseCurrencyUnitId;
        }
        var company = await _companies.FindAsync(companyId);
        return company?.BaseCurrencyUnitId ?? Guid.Empty;
    }

    /// <summary>Birim kodları (CurrencyUnit global → tenant filtresi kapalı).</summary>
    private async Task<Dictionary<Guid, string>> UnitCodesAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (idList.Count == 0) return new();

        using (DataFilter.Disable<IMultiTenant>())
        {
            var rows = await AsyncExecuter.ToListAsync(
                (await _units.GetQueryableAsync())
                    .Where(u => idList.Contains(u.Id))
                    .Select(u => new { u.Id, u.Code }));
            return rows.ToDictionary(x => x.Id, x => x.Code);
        }
    }

    /// <summary>HAS biriminin Id'si — TAKOZ pseudo-birim değerlemesi için (tenant filtresi kapalı; yoksa Guid.Empty).</summary>
    private async Task<Guid> HasUnitIdAsync()
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            return await AsyncExecuter.FirstOrDefaultAsync(
                (await _units.GetQueryableAsync())
                    .Where(u => u.Code == CurrencyUnitCode.HAS)
                    .Select(u => u.Id));
        }
    }
}
