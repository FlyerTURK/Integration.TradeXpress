using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Nakit stok ve hareket raporları. Nakit = İKİ bacak:
/// <list type="bullet">
///   <item><b>Sol bacak</b> (Total / MainUnit) yalnız <see cref="ProcessType.Cash"/> kayıtlarında nakittir.
///         İşaret: Giriş(+)/Çıkış(−).</item>
///   <item><b>Sağ bacak</b> (PayTotal / PayUnit) <b>Peşin</b> (<see cref="ProcessPaymentType.WithCash"/>) ödeme tipli
///         tüm process'lerde nakittir. İşaret tersi: mal Çıkış→nakit girer(+), mal Giriş→nakit çıkar(−).</item>
/// </list>
/// Kapsam Voucher header'ından (Company/Branch/Vault), hiyerarşik opsiyonel (null = alt kırılımları topla).
/// </summary>
[Authorize]
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
        DateTime VoucherDate, long VoucherNumber, ProcessType ProcessType, ProcessDirectionType Direction,
        Guid? VaultId, Guid CompanyId, Guid BranchId, Guid? SubAccountId, string? Description, DateTime CreationTime, Guid LineId);

    public virtual async Task<List<CashStockRowDto>> GetStockAsync(CashReportFilterDto filter)
    {
        var legs = await QueryCashLegsAsync(filter, dateFiltered: false);

        var grouped = legs
            .GroupBy(x => x.UnitId)
            .Select(g => new CashStockRowDto
            {
                UnitId   = g.Key,
                InTotal  = g.Where(x => x.Effect > 0).Sum(x => x.Effect),
                OutTotal = g.Where(x => x.Effect < 0).Sum(x => -x.Effect),
                Net      = g.Sum(x => x.Effect),
            })
            .ToList();

        var unitCodes = await CodeMapAsync(_unitRepository, grouped.Select(r => r.UnitId), u => u.Id, u => u.Code, disableMultiTenant: true);
        foreach (var r in grouped) r.UnitCode = unitCodes.GetValueOrDefault(r.UnitId);
        return grouped.OrderBy(r => r.UnitCode).ToList();
    }

    public virtual async Task<List<CashMovementRowDto>> GetMovementsAsync(CashReportFilterDto filter)
    {
        // Dönem içi satırlar
        var legs = (await QueryCashLegsAsync(filter, dateFiltered: true))
            .OrderBy(x => x.VoucherDate).ThenBy(x => x.CreationTime).ThenBy(x => x.LineId)
            .ToList();

        // Devreden: başlangıç tarihinden önceki tüm birikmiş etki (aynı kapsam + nakit filtresi, tarih hariç)
        var carryLegs = await QueryCashLegsAsync(filter, dateFiltered: false,
            endExclusiveOverride: filter.Start.Date);

        var allLegs    = legs.Concat(carryLegs).ToList();
        var unitCodes    = await CodeMapAsync(_unitRepository,    allLegs.Select(x => x.UnitId),                                    u => u.Id, u => u.Code, disableMultiTenant: true);
        var vaultCodes   = await CodeMapAsync(_vaultRepository,   legs.Where(x => x.VaultId    != null).Select(x => x.VaultId!.Value),   x => x.Id, x => x.Code);
        var branchCodes  = await CodeMapAsync(_branchRepository,  legs.Select(x => x.BranchId),                                     x => x.Id, x => x.Code);
        var companyCodes = await CodeMapAsync(_companyRepository, legs.Select(x => x.CompanyId),                                    x => x.Id, x => x.Code);
        var subCodes     = await CodeMapAsync(_subAccountRepository, legs.Where(x => x.SubAccountId != null).Select(x => x.SubAccountId!.Value), x => x.Id, x => x.Code);

        var result = new List<CashMovementRowDto>();

        // Birim bazında devreden grupları
        var carryByUnit = carryLegs.GroupBy(x => x.UnitId);
        var runningByUnit = new Dictionary<Guid, decimal>();

        foreach (var g in carryByUnit)
        {
            var carry = g.Sum(x => x.Effect);
            runningByUnit[g.Key] = carry;
            if (carry != 0m)
            {
                result.Add(new CashMovementRowDto
                {
                    VoucherDate    = filter.Start.Date,
                    VoucherNumber  = 0,
                    Source         = "Devreden",
                    IsCarryForward = true,
                    UnitId         = g.Key,
                    UnitCode       = unitCodes.GetValueOrDefault(g.Key),
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
                ProcessCode    = Vouchers.VoucherProcessCode.Code(x.ProcessType),
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

    // ── ortak: kapsam + iki-bacak nakit çıkarımı ────────────────────────────────

    private async Task<List<CashLeg>> QueryCashLegsAsync(CashReportFilterDto filter, bool dateFiltered,
        DateTime? endExclusiveOverride = null)
    {
        var start = filter.Start.Date;
        var endExclusive = endExclusiveOverride ?? filter.End.Date.AddDays(1);

        var q = await _voucherRepository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from v in q
            where (filter.CompanyId == null || v.CompanyId == filter.CompanyId)
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
                l.MainUnitId, l.CommodityCode, l.Total,
                l.PayUnitId, l.PayTotal,
                l.Description, l.CreationTime, l.Id,
            });

        var legs = new List<CashLeg>(rows.Count);
        foreach (var r in rows)
        {
            var inflow = ((int)r.Direction % 2) == 0;

            // Sol bacak: yalnız Cash process'te nakit. Giriş + / Çıkış −.
            if (r.Type == ProcessType.Cash && r.MainUnitId != Guid.Empty && r.Total != 0m)
                legs.Add(new CashLeg(r.MainUnitId, inflow ? r.Total : -r.Total, "Nakit",
                    r.CommodityCode,
                    r.VoucherDate, r.VoucherNumber, r.Type, r.Direction, r.VaultId, r.CompanyId, r.BranchId, r.SubAccountId, r.Description, r.CreationTime, r.Id));

            // Sağ bacak: Peşin (WithCash) olan TÜM process'lerde karşılık nakit.
            // CommodityCode = işlemin ANA mali (nakit tanımı değil). Mal Çıkış→nakit girer(+), Giriş→çıkar(−).
            if (r.PaymentType == ProcessPaymentType.WithCash && r.PayUnitId is { } payUnit && r.PayTotal != 0m)
                legs.Add(new CashLeg(payUnit, inflow ? -r.PayTotal : r.PayTotal, "Peşin",
                    r.CommodityCode,
                    r.VoucherDate, r.VoucherNumber, r.Type, r.Direction, r.VaultId, r.CompanyId, r.BranchId, r.SubAccountId, r.Description, r.CreationTime, r.Id));
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
