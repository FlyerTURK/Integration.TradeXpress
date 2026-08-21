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
/// Nakit stok ve hareket raporları. Nakit = İKİ leg (CashLeg):
/// <list type="bullet">
///   <item><b>Sol leg</b> (Total / MainUnit) yalnız <see cref="ProcessType.Cash"/> kayıtlarında nakittir.
///         İşaret: Giriş(+)/Çıkış(−).</item>
///   <item><b>Sağ leg</b> (PayTotal / PayUnit) <b>Peşin</b> (<see cref="ProcessPaymentType.WithCash"/>) ödeme tipli
///         tüm process'lerde nakittir. İşaret tersi: mal Çıkış→nakit girer(+), mal Giriş→nakit çıkar(−).</item>
/// </list>
/// Kapsam Voucher header'ından (Company/Branch/Vault), hiyerarşik opsiyonel (null = alt kırılımları topla).
/// </summary>
[Authorize(TradeXpressPermissions.Reports.Cash)]
public class CashReportAppService : TradeXpressAppService, ICashReportAppService
{
    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IDataFilter _dataFilter;

    public CashReportAppService(
        IRepository<Voucher, Guid> voucherRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Company, Guid> companyRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IDataFilter dataFilter)
    {
        _voucherRepository = voucherRepository;
        _vaultRepository = vaultRepository;
        _branchRepository = branchRepository;
        _companyRepository = companyRepository;
        _unitRepository = unitRepository;
        _subAccountRepository = subAccountRepository;
        _dataFilter = dataFilter;
    }

    private sealed record CashLeg(Guid UnitId, decimal Effect, string Source,
        string? MainCommodityCode,
        DateTime VoucherDate, long VoucherNumber, ProcessType ProcessType, ProcessDirectionType Direction, ProcessPaymentType? PaymentType,
        Guid? VaultId, Guid CompanyId, Guid BranchId, Guid? SubAccountId, string? Description, DateTime CreationTime, Guid LineId);

    /// <summary>
    /// Nakit STOK: kapsam (şirket DAİMA ICurrentCompany'den) + branch/vault, TÜM geçmişin birim-bazlı Giren/Çıkan/Net'i.
    /// K4: satırları belleğe çekip in-memory leg üretmek yerine SQL-side GROUP BY + SUM (<see cref="GetCashNetByUnitAsync"/>
    /// deseni) — iki leg / işaret / CashId / kapsam kuralları <see cref="QueryCashLegsAsync"/> ile BİREBİR aynı, yalnız
    /// aggregation DB'de. Giren = Σ(leg-effect &gt; 0), Çıkan = Σ(−effect &lt; 0); Net = Giren − Çıkan (= Σ effect).
    /// </summary>
    public virtual async Task<List<CashStockRowDto>> GetStockAsync(CashReportFilterDto filter)
    {
        // SIZINTI ÖNLEME: rapor DAİMA çalışılan şirketle sınırlı (ICurrentCompany). Yoksa (host/API) boş.
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
            return new List<CashStockRowDto>();

        var q = await _voucherRepository.GetQueryableAsync();

        // Sol leg: yalnız Cash process'te nakit (Total @ MainUnit). Giriş + / Çıkış −.
        // IQueryable → SQL: IsInflow() extension'ı EF Core tarafından çevrilemez, ham %2 (giriş = çift) bilinçli.
        // effect = giriş ? Total : −Total; Giren = effect>0 payı, Çıkan = effect<0 payı (mutlak).
        var mainAgg = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (filter.BranchId == null || v.BranchId == filter.BranchId)
               && (filter.VaultId == null || v.VaultId == filter.VaultId)
            from l in v.Lines
            where !l.IsDeleted && l.Type == ProcessType.Cash && l.MainUnitId != Guid.Empty && l.Total != 0m
               && (filter.CashId == null || l.CommodityId == filter.CashId)
            group l by l.MainUnitId into g
            select new
            {
                UnitId = g.Key,
                In  = g.Sum(x => (((int)x.Direction % 2) == 0 ? x.Total : -x.Total) > 0m
                                 ? (((int)x.Direction % 2) == 0 ? x.Total : -x.Total) : 0m),
                Out = g.Sum(x => (((int)x.Direction % 2) == 0 ? x.Total : -x.Total) < 0m
                                 ? (((int)x.Direction % 2) == 0 ? -x.Total : x.Total) : 0m),
            });

        // Sağ leg: Peşin (WithCash) olan tüm process'lerde karşılık nakit (PayTotal @ PayUnit).
        // İşaret tersi: mal Çıkış → nakit girer (+), mal Giriş → nakit çıkar (−) → effect = giriş ? −PayTotal : PayTotal.
        var payAgg = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (filter.BranchId == null || v.BranchId == filter.BranchId)
               && (filter.VaultId == null || v.VaultId == filter.VaultId)
            from l in v.Lines
            where !l.IsDeleted && l.PaymentType == ProcessPaymentType.WithCash && l.PayUnitId != null && l.PayTotal != 0m
               && (filter.CashId == null || l.PayCommodityId == filter.CashId)
            group l by l.PayUnitId into g
            select new
            {
                UnitId = g.Key,
                In  = g.Sum(x => (((int)x.Direction % 2) == 0 ? -x.PayTotal : x.PayTotal) > 0m
                                 ? (((int)x.Direction % 2) == 0 ? -x.PayTotal : x.PayTotal) : 0m),
                Out = g.Sum(x => (((int)x.Direction % 2) == 0 ? -x.PayTotal : x.PayTotal) < 0m
                                 ? (((int)x.Direction % 2) == 0 ? x.PayTotal : -x.PayTotal) : 0m),
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

        var grouped = byUnit
            .Select(kv => new CashStockRowDto
            {
                UnitId   = kv.Key,
                InTotal  = kv.Value.In,
                OutTotal = kv.Value.Out,
                Net      = kv.Value.In - kv.Value.Out,
            })
            .ToList();

        var unitCodes = await CodeMapAsync(_unitRepository, grouped.Select(r => r.UnitId), u => u.Id, u => u.Code, disableMultiTenant: true);
        foreach (var r in grouped) r.UnitCode = unitCodes.GetValueOrDefault(r.UnitId);
        return grouped.OrderBy(r => r.UnitCode).ToList();
    }

    /// <summary>
    /// Bilanço STOK (nakit) kategorisi için fiziksel nakit holding'i: kapsam (şirket DAİMA ICurrentCompany'den) + branch/
    /// vault, <paramref name="asOfExclusive"/> tarihinden ÖNCE birikmiş net, birim-bazında. Net = FİRMA perspektifi
    /// (+ = firma o nakdi tutar). K4: satırları belleğe çekmek yerine SQL-side GROUP BY + SUM (AccountBalance/ServicePL
    /// ledger deseni) — leg/işaret kuralları <see cref="QueryCashLegsAsync"/> ile BİREBİR aynı, yalnız aggregation DB'de.
    /// </summary>
    public virtual async Task<Dictionary<Guid, decimal>> GetCashNetByUnitAsync(Guid? branchId, Guid? vaultId, DateTime asOfExclusive)
    {
        // SIZINTI ÖNLEME: rapor DAİMA çalışılan şirketle sınırlı (ICurrentCompany). Yoksa (host/API) boş.
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
            return new Dictionary<Guid, decimal>();

        var q = await _voucherRepository.GetQueryableAsync();

        // Sol leg: yalnız Cash process'te nakit (Total @ MainUnit). Giriş + / Çıkış −.
        var mainLegs = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (branchId == null || v.BranchId == branchId)
               && (vaultId == null || v.VaultId == vaultId)
               && v.VoucherDate < asOfExclusive
            from l in v.Lines
            where !l.IsDeleted && l.Type == ProcessType.Cash && l.MainUnitId != Guid.Empty && l.Total != 0m
            group l by l.MainUnitId into g
            // IQueryable → SQL: IsInflow() extension'ı EF Core tarafından çevrilemez, ham %2 bilinçli.
            select new { UnitId = g.Key, Net = g.Sum(x => ((int)x.Direction % 2) == 0 ? x.Total : -x.Total) });

        // Sağ leg: Peşin (WithCash) olan tüm process'lerde karşılık nakit (PayTotal @ PayUnit).
        // İşaret tersi: mal Çıkış → nakit girer (+PayTotal), mal Giriş → nakit çıkar (−PayTotal).
        var payLegs = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (branchId == null || v.BranchId == branchId)
               && (vaultId == null || v.VaultId == vaultId)
               && v.VoucherDate < asOfExclusive
            from l in v.Lines
            where !l.IsDeleted && l.PaymentType == ProcessPaymentType.WithCash && l.PayUnitId != null && l.PayTotal != 0m
            group l by l.PayUnitId into g
            // IQueryable → SQL: IsInflow() extension'ı EF Core tarafından çevrilemez, ham %2 bilinçli.
            select new { UnitId = g.Key, Net = g.Sum(x => ((int)x.Direction % 2) == 0 ? -x.PayTotal : x.PayTotal) });

        var result = mainLegs.ToDictionary(r => r.UnitId, r => r.Net);
        foreach (var r in payLegs)
        {
            var unitId = r.UnitId!.Value;   // where-filtresi null'ı zaten eledi
            result[unitId] = result.GetValueOrDefault(unitId) + r.Net;
        }
        return result;
    }

    public virtual async Task<List<CashMovementRowDto>> GetMovementsAsync(CashReportFilterDto filter)
    {
        // Dönem içi satırlar — DETAY liste (satır-satır KALIR, aggregate edilmez).
        var legs = (await QueryCashLegsAsync(filter, dateFiltered: true))
            .OrderBy(x => x.VoucherDate).ThenBy(x => x.CreationTime).ThenBy(x => x.LineId)
            .ToList();

        // Devreden: başlangıç tarihinden önceki tüm birikmiş NET (aynı kapsam + nakit filtresi). K4: satırları belleğe
        // çekmek yerine SQL-side GROUP BY + SUM (birim → net) — devreden yalnız birim-bazlı toplam olduğundan detay
        // gerekmez; leg/işaret/CashId/kapsam kuralları QueryCashLegsAsync ile BİREBİR aynı.
        var carryNet = await QueryCashCarryNetAsync(filter, filter.Start.Date);

        var unitCodes    = await CodeMapAsync(_unitRepository,    legs.Select(x => x.UnitId).Concat(carryNet.Keys),                 u => u.Id, u => u.Code, disableMultiTenant: true);
        var vaultCodes   = await CodeMapAsync(_vaultRepository,   legs.Where(x => x.VaultId    != null).Select(x => x.VaultId!.Value),   x => x.Id, x => x.Code);
        var branchCodes  = await CodeMapAsync(_branchRepository,  legs.Select(x => x.BranchId),                                     x => x.Id, x => x.Code);
        var companyCodes = await CodeMapAsync(_companyRepository, legs.Select(x => x.CompanyId),                                    x => x.Id, x => x.Code);
        var subCodes     = await CodeMapAsync(_subAccountRepository, legs.Where(x => x.SubAccountId != null).Select(x => x.SubAccountId!.Value), x => x.Id, x => x.Code);

        var result = new List<CashMovementRowDto>();

        // Birim bazında devreden (SQL-side hesaplı net); running her devreden birim için seed'lenir (net 0 olsa da).
        var runningByUnit = new Dictionary<Guid, decimal>();

        foreach (var (unitId, carry) in carryNet)
        {
            runningByUnit[unitId] = carry;
            if (carry != 0m)
            {
                result.Add(new CashMovementRowDto
                {
                    VoucherDate    = filter.Start.Date,
                    VoucherNumber  = 0,
                    Source         = "Devreden",
                    IsCarryForward = true,
                    UnitId         = unitId,
                    UnitCode       = unitCodes.GetValueOrDefault(unitId),
                    CashAmount     = carry,
                    RunningBalance = carry,
                });
            }
        }

        // Dönem hareketleri + cari bakiye
        foreach (var x in legs)
        {
            runningByUnit.TryGetValue(x.UnitId, out var prev);
            var running = prev + x.Effect;
            runningByUnit[x.UnitId] = running;

            result.Add(new CashMovementRowDto
            {
                VoucherDate    = x.VoucherDate,
                VoucherNumber  = x.VoucherNumber,
                ProcessType    = x.ProcessType,
                ProcessCode    = Vouchers.VoucherProcessCode.Of(x.ProcessType, x.Direction, x.PaymentType),
                Source         = x.Source,
                CompanyCode    = companyCodes.GetValueOrDefault(x.CompanyId),
                BranchCode     = branchCodes.GetValueOrDefault(x.BranchId),
                VaultCode      = x.VaultId is { } v ? vaultCodes.GetValueOrDefault(v) : null,
                SubAccountCode = x.SubAccountId is { } s ? subCodes.GetValueOrDefault(s) : null,
                Direction      = x.Direction,
                CommodityCode  = x.MainCommodityCode,
                UnitId         = x.UnitId,
                UnitCode       = unitCodes.GetValueOrDefault(x.UnitId),
                CashAmount     = x.Effect,
                RunningBalance = running,
                Description    = x.Description,
            });
        }

        return result;
    }

    // ── ortak: kapsam + iki-leg nakit çıkarımı ────────────────────────────────

    /// <summary>
    /// Devreden (hareket raporu): <paramref name="endExclusive"/> tarihinden ÖNCEKİ birikmiş NET, birim-bazında,
    /// SQL-side GROUP BY + SUM (<see cref="GetCashNetByUnitAsync"/> deseni + CashId/kapsam filtresi). İki leg / işaret /
    /// CashId kuralları <see cref="QueryCashLegsAsync"/> ile BİREBİR aynı, yalnız aggregation DB'de. Yalnız net döner
    /// (devreden tek satır → In/Out ayrımı gerekmez).
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> QueryCashCarryNetAsync(CashReportFilterDto filter, DateTime endExclusive)
    {
        // SIZINTI ÖNLEME: rapor DAİMA çalışılan şirketle sınırlı (ICurrentCompany). Yoksa (host/API) boş.
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
            return new Dictionary<Guid, decimal>();

        var q = await _voucherRepository.GetQueryableAsync();

        // Sol leg: yalnız Cash process'te nakit (Total @ MainUnit). Giriş + / Çıkış −.
        // IQueryable → SQL: IsInflow() extension'ı EF Core tarafından çevrilemez, ham %2 (giriş = çift) bilinçli.
        var mainLegs = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (filter.BranchId == null || v.BranchId == filter.BranchId)
               && (filter.VaultId == null || v.VaultId == filter.VaultId)
               && v.VoucherDate < endExclusive
            from l in v.Lines
            where !l.IsDeleted && l.Type == ProcessType.Cash && l.MainUnitId != Guid.Empty && l.Total != 0m
               && (filter.CashId == null || l.CommodityId == filter.CashId)
            group l by l.MainUnitId into g
            select new { UnitId = g.Key, Net = g.Sum(x => ((int)x.Direction % 2) == 0 ? x.Total : -x.Total) });

        // Sağ leg: Peşin (WithCash) tüm process'lerde karşılık nakit (PayTotal @ PayUnit).
        // İşaret tersi: mal Çıkış → nakit girer (+), mal Giriş → nakit çıkar (−).
        var payLegs = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (filter.BranchId == null || v.BranchId == filter.BranchId)
               && (filter.VaultId == null || v.VaultId == filter.VaultId)
               && v.VoucherDate < endExclusive
            from l in v.Lines
            where !l.IsDeleted && l.PaymentType == ProcessPaymentType.WithCash && l.PayUnitId != null && l.PayTotal != 0m
               && (filter.CashId == null || l.PayCommodityId == filter.CashId)
            group l by l.PayUnitId into g
            select new { UnitId = g.Key, Net = g.Sum(x => ((int)x.Direction % 2) == 0 ? -x.PayTotal : x.PayTotal) });

        var result = mainLegs.ToDictionary(r => r.UnitId, r => r.Net);
        foreach (var r in payLegs)
        {
            var unitId = r.UnitId!.Value;   // where-filtresi null'ı zaten eledi
            result[unitId] = result.GetValueOrDefault(unitId) + r.Net;
        }
        return result;
    }

    private async Task<List<CashLeg>> QueryCashLegsAsync(CashReportFilterDto filter, bool dateFiltered,
        DateTime? endExclusiveOverride = null)
    {
        // SIZINTI ÖNLEME: rapor DAİMA çalışılan şirketle sınırlı (ICurrentCompany). Yoksa (host/API) boş.
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
            return new List<CashLeg>();

        var start = filter.Start.Date;
        var endExclusive = endExclusiveOverride ?? filter.End.Date.AddDays(1);

        var q = await _voucherRepository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (filter.BranchId == null || v.BranchId == filter.BranchId)
               && (filter.VaultId == null || v.VaultId == filter.VaultId)
               && (!dateFiltered && endExclusiveOverride == null
                   || (dateFiltered && v.VoucherDate >= start && v.VoucherDate < endExclusive)
                   || (endExclusiveOverride != null && v.VoucherDate < endExclusive))
            from l in v.Lines
            where !l.IsDeleted
               && (l.Type == ProcessType.Cash || l.PaymentType == ProcessPaymentType.WithCash)
               && (filter.CashId == null || l.CommodityId == filter.CashId || l.PayCommodityId == filter.CashId)
            select new
            {
                v.VoucherDate, v.VoucherNumber, v.VaultId, v.CompanyId, v.BranchId, v.SubAccountId,
                l.Type, l.PaymentType, l.Direction,
                l.MainUnitId, l.CommodityId, l.CommodityCode, l.Total,
                l.PayUnitId, l.PayCommodityId, l.PayTotal,
                l.Description, l.CreationTime, l.Id,
            });

        var legs = new List<CashLeg>(rows.Count);
        foreach (var r in rows)
        {
            var inflow = r.Direction.IsInflow();

            // Sol leg: yalnız Cash process'te nakit. Giriş + / Çıkış −.
            // CashId filtresi varsa yalnız bu bacağın nakiti eşleşiyorsa oluştur.
            if (r.Type == ProcessType.Cash && r.MainUnitId != Guid.Empty && r.Total != 0m
                && (filter.CashId == null || r.CommodityId == filter.CashId))
                legs.Add(new CashLeg(r.MainUnitId, inflow ? r.Total : -r.Total, "Nakit",
                    r.CommodityCode,
                    r.VoucherDate, r.VoucherNumber, r.Type, r.Direction, r.PaymentType, r.VaultId, r.CompanyId, r.BranchId, r.SubAccountId, r.Description, r.CreationTime, r.Id));

            // Sağ leg: Peşin (WithCash) olan tüm process'lerde karşılık nakit.
            // CashId filtresi varsa yalnız bu bacağın nakiti (PayCommodityId) eşleşiyorsa oluştur.
            if (r.PaymentType == ProcessPaymentType.WithCash && r.PayUnitId is { } payUnit && r.PayTotal != 0m
                && (filter.CashId == null || r.PayCommodityId == filter.CashId))
                legs.Add(new CashLeg(payUnit, inflow ? -r.PayTotal : r.PayTotal, "Peşin",
                    r.CommodityCode,
                    r.VoucherDate, r.VoucherNumber, r.Type, r.Direction, r.PaymentType, r.VaultId, r.CompanyId, r.BranchId, r.SubAccountId, r.Description, r.CreationTime, r.Id));
        }

        return legs;
    }

    private async Task<Dictionary<Guid, string>> CodeMapAsync<T>(
        IRepository<T, Guid> repo, IEnumerable<Guid> ids, Func<T, Guid> keyOf, Func<T, string> codeOf,
        bool disableMultiTenant = false)
        where T : class, Volo.Abp.Domain.Entities.IEntity<Guid>
    {
        var idList = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (idList.Count == 0) return new();
        if (disableMultiTenant)
        {
            using (_dataFilter.Disable<IMultiTenant>())
            {
                var rows2 = await AsyncExecuter.ToListAsync((await repo.GetQueryableAsync()).Where(x => idList.Contains(x.Id)));
                return rows2.ToDictionary(keyOf, codeOf);
            }
        }
        var rows = await AsyncExecuter.ToListAsync((await repo.GetQueryableAsync()).Where(x => idList.Contains(x.Id)));
        return rows.ToDictionary(keyOf, codeOf);
    }
}
