using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Hurda stok ve hareket raporları. Bakiye etkisi ScrapBalancePoster ile aynı kural:
/// Peşin → etki yok; Bedelli → PayTotal@PayUnit; Normal/diğer → Total@MainUnit (Has).
/// </summary>
[Authorize(TradeXpressPermissions.Reports.Scrap)]
public class ScrapReportAppService : TradeXpressAppService, IScrapReportAppService
{
    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IDataFilter _dataFilter;

    public ScrapReportAppService(
        IRepository<Voucher, Guid> voucherRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Company, Guid> companyRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IDataFilter dataFilter)
    {
        _voucherRepository = voucherRepository;
        _vaultRepository   = vaultRepository;
        _branchRepository  = branchRepository;
        _companyRepository = companyRepository;
        _unitRepository    = unitRepository;
        _subAccountRepository = subAccountRepository;
        _dataFilter = dataFilter;
    }

    private sealed record ScrapLeg(Guid UnitId, decimal Effect,
        decimal Amount, decimal Factor,
        string? CommodityCode, ProcessPaymentType? PaymentType,
        DateTime VoucherDate, long VoucherNumber, ProcessType ProcessType, ProcessDirectionType Direction,
        Guid? VaultId, Guid CompanyId, Guid BranchId, Guid? SubAccountId, string? Description, DateTime CreationTime, Guid LineId);

    /// <summary>
    /// Hurda STOK: kapsam (şirket DAİMA ICurrentCompany'den) + branch/vault, TÜM geçmişin birim-bazlı Giren/Çıkan/Net'i.
    /// K4: satırları belleğe çekip in-memory leg üretmek yerine SQL-side GROUP BY + SUM — bacak/işaret/kapsam/ScrapId
    /// kuralları <see cref="QueryLegsAsync"/> ile BİREBİR aynı (yalnız aggregation DB'de): Peşin (WithCash) yansımaz;
    /// Bedelli (WithCurrency) → PayUnit@PayTotal; diğer (Normal/İade/Emanet/null) → MainUnit@Amount. effect = giriş ? +:−.
    /// Giren = Σ(effect &gt; 0), Çıkan = Σ(−effect &lt; 0); Net = Giren − Çıkan (= Σ effect).
    /// </summary>
    public virtual async Task<List<ScrapStockRowDto>> GetStockAsync(ScrapReportFilterDto filter)
    {
        // SIZINTI ÖNLEME: rapor DAİMA çalışılan şirketle sınırlı (ICurrentCompany). Yoksa (host/API) boş.
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
            return new List<ScrapStockRowDto>();

        var q = await _voucherRepository.GetQueryableAsync();

        // Ana bacak: Normal/İade/Emanet (Peşin ve Bedelli HARİÇ) → MainUnit@Amount. effect = giriş ? Amount : −Amount.
        // IQueryable → SQL: IsInflow() çevrilemez, ham %2 (giriş = çift) bilinçli. PaymentType != X karşılaştırmaları
        // EF null-telafisiyle (NULL PaymentType dahil) QueryLegsAsync'in isBedelli=false dalıyla birebir.
        var mainAgg = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (filter.BranchId == null || v.BranchId == filter.BranchId)
               && (filter.VaultId  == null || v.VaultId  == filter.VaultId)
            from l in v.Lines
            where !l.IsDeleted && l.Type == ProcessType.Scrap
               && l.PaymentType != ProcessPaymentType.WithCash
               && l.PaymentType != ProcessPaymentType.WithCurrency
               && l.MainUnitId != Guid.Empty && l.Amount != 0m
               && (filter.ScrapId == null || l.CommodityId == filter.ScrapId)
            group l by l.MainUnitId into g
            select new
            {
                UnitId = g.Key,
                In  = g.Sum(x => (((int)x.Direction % 2) == 0 ? x.Amount : -x.Amount) > 0m
                                 ? (((int)x.Direction % 2) == 0 ? x.Amount : -x.Amount) : 0m),
                Out = g.Sum(x => (((int)x.Direction % 2) == 0 ? x.Amount : -x.Amount) < 0m
                                 ? (((int)x.Direction % 2) == 0 ? -x.Amount : x.Amount) : 0m),
            });

        // Bedelli bacak: WithCurrency → PayUnit@PayTotal. effect = giriş ? PayTotal : −PayTotal.
        var payAgg = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (filter.BranchId == null || v.BranchId == filter.BranchId)
               && (filter.VaultId  == null || v.VaultId  == filter.VaultId)
            from l in v.Lines
            where !l.IsDeleted && l.Type == ProcessType.Scrap
               && l.PaymentType == ProcessPaymentType.WithCurrency
               && l.PayUnitId != null && l.PayUnitId != Guid.Empty && l.PayTotal != 0m
               && (filter.ScrapId == null || l.CommodityId == filter.ScrapId)
            group l by l.PayUnitId into g
            select new
            {
                UnitId = g.Key,
                In  = g.Sum(x => (((int)x.Direction % 2) == 0 ? x.PayTotal : -x.PayTotal) > 0m
                                 ? (((int)x.Direction % 2) == 0 ? x.PayTotal : -x.PayTotal) : 0m),
                Out = g.Sum(x => (((int)x.Direction % 2) == 0 ? x.PayTotal : -x.PayTotal) < 0m
                                 ? (((int)x.Direction % 2) == 0 ? -x.PayTotal : x.PayTotal) : 0m),
            });

        // İki bacağı birim-bazında birleştir (Giren/Çıkan ayrı toplanır; Net = Giren − Çıkan — in-memory ile birebir).
        var byUnit = new Dictionary<Guid, (decimal In, decimal Out)>();
        foreach (var r in mainAgg)
        {
            var cur = byUnit.GetValueOrDefault(r.UnitId);
            byUnit[r.UnitId] = (cur.In + r.In, cur.Out + r.Out);
        }
        foreach (var r in payAgg)
        {
            var unitId = r.UnitId!.Value;   // where-filtresi null'ı zaten eledi
            var cur = byUnit.GetValueOrDefault(unitId);
            byUnit[unitId] = (cur.In + r.In, cur.Out + r.Out);
        }

        var grouped = byUnit.Select(kv => new ScrapStockRowDto
        {
            UnitId   = kv.Key,
            InTotal  = kv.Value.In,
            OutTotal = kv.Value.Out,
            Net      = kv.Value.In - kv.Value.Out,
        }).ToList();

        var unitCodes = await UnitCodesAsync(grouped.Select(r => r.UnitId));
        foreach (var r in grouped) r.UnitCode = unitCodes.GetValueOrDefault(r.UnitId);
        return grouped.OrderBy(r => r.UnitCode).ToList();
    }

    /// <summary>
    /// Bilanço STOK(hurda) için fiziksel hurda-maden holding'i: kapsam (şirket DAİMA ICurrentCompany'den) + branch/vault,
    /// asOfExclusive'den ÖNCE birikmiş net, MainUnit(HAS)-bazında. HAS içeriği = <b>Total</b> (= Amount × Factor; poster'ın
    /// Normal'de cari'ye yazdığı Total ile birebir offset). TÜM ödeme tipleri (Peşin dahil — fiziksel hurda hareket eder).
    /// GetStockAsync ödeme-tipine göre cari/fiziksel KARIŞIK döner (Bedelli→PayUnit cari) → bu metod yalnız fiziksel ana
    /// bacağı (MainUnit/Total) alır. + = firma o hurdayı tutar. Değerleme merkezde.
    /// </summary>
    public virtual async Task<Dictionary<Guid, decimal>> GetScrapNetByUnitAsync(Guid? branchId, Guid? vaultId, DateTime asOfExclusive)
    {
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
            return new Dictionary<Guid, decimal>();

        // K4: satırları belleğe çekmeden SQL-side GROUP BY + koşullu SUM (ledger deseni).
        var q = await _voucherRepository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (branchId == null || v.BranchId == branchId)
               && (vaultId == null || v.VaultId == vaultId)
               && v.VoucherDate < asOfExclusive
            from l in v.Lines
            where !l.IsDeleted && l.Type == ProcessType.Scrap && l.MainUnitId != Guid.Empty && l.Total != 0m
            group l by l.MainUnitId into g
            // IQueryable → SQL: IsInflow() extension'ı EF Core tarafından çevrilemez, ham %2 bilinçli.
            select new { UnitId = g.Key, Net = g.Sum(x => ((int)x.Direction % 2) == 0 ? x.Total : -x.Total) });

        return rows.ToDictionary(r => r.UnitId, r => r.Net);
    }

    /// <summary>DRILL — hurda stok, COMMODITY bazında, tek birim için (bilanço Stok popup). GetScrapNetByUnitAsync paritesi, kod kırılımı.</summary>
    public virtual async Task<Dictionary<string, decimal>> GetScrapStockByCommodityAsync(Guid? branchId, Guid unitId, DateTime asOfExclusive)
    {
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
            return new Dictionary<string, decimal>();

        var q = await _voucherRepository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (branchId == null || v.BranchId == branchId)
               && v.VoucherDate < asOfExclusive
            from l in v.Lines
            where !l.IsDeleted && l.Type == ProcessType.Scrap && l.MainUnitId == unitId && l.Total != 0m
            select new { l.CommodityCode, l.Direction, l.Total });

        return rows
            .GroupBy(r => r.CommodityCode ?? "?")
            .Select(g => new { Code = g.Key, Net = g.Sum(r => (r.Direction.IsInflow() ? 1m : -1m) * r.Total) })
            .Where(x => x.Net != 0m)
            .ToDictionary(x => x.Code, x => x.Net);
    }

    public virtual async Task<List<ScrapMovementRowDto>> GetMovementsAsync(ScrapReportFilterDto filter)
    {
        var legs      = (await QueryLegsAsync(filter, dateFiltered: true))
            .OrderBy(x => x.VoucherDate).ThenBy(x => x.CreationTime).ThenBy(x => x.LineId).ToList();
        var carryLegs = await QueryLegsAsync(filter, dateFiltered: false, endExclusiveOverride: filter.Start.Date);

        var unitCodes    = await UnitCodesAsync(legs.Concat(carryLegs).Select(x => x.UnitId));
        var vaultCodes   = await CodeMapAsync(_vaultRepository,   legs.Where(x => x.VaultId    != null).Select(x => x.VaultId!.Value),    x => x.Id, x => x.Code);
        var branchCodes  = await CodeMapAsync(_branchRepository,  legs.Select(x => x.BranchId),  x => x.Id, x => x.Code);
        var companyCodes = await CodeMapAsync(_companyRepository, legs.Select(x => x.CompanyId), x => x.Id, x => x.Code);
        var subCodes     = await CodeMapAsync(_subAccountRepository, legs.Where(x => x.SubAccountId != null).Select(x => x.SubAccountId!.Value), x => x.Id, x => x.Code);

        var result = new List<ScrapMovementRowDto>();
        var running = new Dictionary<Guid, decimal>();

        foreach (var g in carryLegs.GroupBy(x => x.UnitId))
        {
            var carry = g.Sum(x => x.Effect);
            running[g.Key] = carry;
            if (carry != 0m)
                result.Add(new ScrapMovementRowDto
                {
                    VoucherDate    = filter.Start.Date,
                    IsCarryForward = true,
                    Source         = "Devreden",
                    UnitId         = g.Key,
                    UnitCode       = unitCodes.GetValueOrDefault(g.Key),
                    Effect         = carry,
                    RunningBalance = carry,
                });
        }

        foreach (var x in legs)
        {
            running.TryGetValue(x.UnitId, out var prev);
            var rb = prev + x.Effect;
            running[x.UnitId] = rb;

            result.Add(new ScrapMovementRowDto
            {
                VoucherDate    = x.VoucherDate,
                VoucherNumber  = x.VoucherNumber,
                ProcessType    = x.ProcessType,
                ProcessCode    = VoucherProcessCode.Of(x.ProcessType, x.Direction, x.PaymentType),
                CompanyCode    = companyCodes.GetValueOrDefault(x.CompanyId),
                BranchCode     = branchCodes.GetValueOrDefault(x.BranchId),
                VaultCode      = x.VaultId is { } v ? vaultCodes.GetValueOrDefault(v) : null,
                SubAccountCode = x.SubAccountId is { } s ? subCodes.GetValueOrDefault(s) : null,
                Direction      = x.Direction,
                CommodityCode  = x.CommodityCode,
                UnitId         = x.UnitId,
                UnitCode       = unitCodes.GetValueOrDefault(x.UnitId),
                Amount         = x.Amount,
                Factor         = x.Factor,
                Effect         = x.Effect,
                RunningBalance = rb,
                Description    = x.Description,
            });
        }

        return result;
    }

    private async Task<List<ScrapLeg>> QueryLegsAsync(ScrapReportFilterDto filter, bool dateFiltered,
        DateTime? endExclusiveOverride = null)
    {
        // SIZINTI ÖNLEME: rapor DAİMA çalışılan şirketle sınırlı (ICurrentCompany). Yoksa (host/API) boş.
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
            return new List<ScrapLeg>();

        var start        = filter.Start.Date;
        var endExclusive = endExclusiveOverride ?? filter.End.Date.AddDays(1);

        var q = await _voucherRepository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (filter.BranchId  == null || v.BranchId  == filter.BranchId)
               && (filter.VaultId   == null || v.VaultId   == filter.VaultId)
               && (!dateFiltered && endExclusiveOverride == null
                   || (dateFiltered && v.VoucherDate >= start && v.VoucherDate < endExclusive)
                   || (endExclusiveOverride != null && v.VoucherDate < endExclusive))
            from l in v.Lines
            where !l.IsDeleted
               && l.Type == ProcessType.Scrap
               && l.PaymentType != ProcessPaymentType.WithCash   // Peşin bakiyeye girmez
               && (filter.ScrapId == null || l.CommodityId == filter.ScrapId)
            select new
            {
                v.VoucherDate, v.VoucherNumber, v.VaultId, v.CompanyId, v.BranchId, v.SubAccountId,
                l.Type, l.PaymentType, l.Direction,
                l.MainUnitId, l.CommodityCode, l.Amount, l.Factor, l.Total,
                l.PayUnitId, l.PayTotal,
                l.Description, l.CreationTime, l.Id,
            });

        return rows.Select(r =>
        {
            var inflow = r.Direction.IsInflow();
            var sign   = inflow ? 1m : -1m;

            bool isBedelli = r.PaymentType == ProcessPaymentType.WithCurrency;
            var unitId = isBedelli ? (r.PayUnitId ?? Guid.Empty) : r.MainUnitId;
            var effect = isBedelli ? sign * r.PayTotal : sign * r.Amount;

            return new ScrapLeg(unitId, effect,
                r.Amount, r.Factor,
                r.CommodityCode, r.PaymentType,
                r.VoucherDate, r.VoucherNumber, r.Type, r.Direction,
                r.VaultId, r.CompanyId, r.BranchId, r.SubAccountId, r.Description, r.CreationTime, r.Id);
        }).Where(x => x.UnitId != Guid.Empty && x.Effect != 0m).ToList();
    }

    private async Task<Dictionary<Guid, string>> UnitCodesAsync(IEnumerable<Guid> ids)
        => await CodeMapAsync(_unitRepository, ids, u => u.Id, u => u.Code, disableMultiTenant: true);

    private async Task<Dictionary<Guid, string>> CodeMapAsync<T>(
        IRepository<T, Guid> repo, IEnumerable<Guid> ids, Func<T, Guid> keyOf, Func<T, string> codeOf,
        bool disableMultiTenant = false)
        where T : class, Volo.Abp.Domain.Entities.IEntity<Guid>
    {
        var idList = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (idList.Count == 0) return new();
        if (disableMultiTenant)
            using (_dataFilter.Disable<IMultiTenant>())
            {
                var r = await AsyncExecuter.ToListAsync((await repo.GetQueryableAsync()).Where(x => idList.Contains(x.Id)));
                return r.ToDictionary(keyOf, codeOf);
            }
        var rows = await AsyncExecuter.ToListAsync((await repo.GetQueryableAsync()).Where(x => idList.Contains(x.Id)));
        return rows.ToDictionary(keyOf, codeOf);
    }
}
