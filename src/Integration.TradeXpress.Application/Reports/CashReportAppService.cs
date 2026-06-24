using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
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
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IDataFilter _dataFilter;

    public CashReportAppService(
        IRepository<Voucher, Guid> voucherRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IDataFilter dataFilter)
    {
        _voucherRepository = voucherRepository;
        _vaultRepository = vaultRepository;
        _unitRepository = unitRepository;
        _subAccountRepository = subAccountRepository;
        _dataFilter = dataFilter;
    }

    private sealed record CashLeg(Guid UnitId, decimal Effect, string? CashCode, string Source,
        DateTime VoucherDate, long VoucherNumber, ProcessType ProcessType, ProcessDirectionType Direction,
        Guid? VaultId, Guid? SubAccountId, string? Description, DateTime CreationTime, Guid LineId);

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
        var legs = (await QueryCashLegsAsync(filter, dateFiltered: true))
            .OrderBy(x => x.VoucherDate).ThenBy(x => x.CreationTime).ThenBy(x => x.LineId)
            .ToList();

        var unitCodes = await CodeMapAsync(_unitRepository, legs.Select(x => x.UnitId), u => u.Id, u => u.Code, disableMultiTenant: true);
        var vaultCodes = await CodeMapAsync(_vaultRepository, legs.Where(x => x.VaultId != null).Select(x => x.VaultId!.Value), x => x.Id, x => x.Code);
        var subCodes = await CodeMapAsync(_subAccountRepository, legs.Where(x => x.SubAccountId != null).Select(x => x.SubAccountId!.Value), x => x.Id, x => x.Code);

        return legs.Select(x => new CashMovementRowDto
        {
            VoucherDate    = x.VoucherDate,
            VoucherNumber  = x.VoucherNumber,
            ProcessType    = x.ProcessType,
            Source         = x.Source,
            VaultCode      = x.VaultId is { } v ? vaultCodes.GetValueOrDefault(v) : null,
            SubAccountCode = x.SubAccountId is { } s ? subCodes.GetValueOrDefault(s) : null,
            Direction      = x.Direction,
            CashCode       = x.CashCode,
            UnitId         = x.UnitId,
            UnitCode       = unitCodes.GetValueOrDefault(x.UnitId),
            CashAmount     = x.Effect,
            Description    = x.Description,
        }).ToList();
    }

    // ── ortak: kapsam + iki-bacak nakit çıkarımı ────────────────────────────────

    private async Task<List<CashLeg>> QueryCashLegsAsync(CashReportFilterDto filter, bool dateFiltered)
    {
        var start = filter.Start.Date;
        var endExclusive = filter.End.Date.AddDays(1);

        var q = await _voucherRepository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from v in q
            where (filter.CompanyId == null || v.CompanyId == filter.CompanyId)
               && (filter.BranchId == null || v.BranchId == filter.BranchId)
               && (filter.VaultId == null || v.VaultId == filter.VaultId)
               && (!dateFiltered || (v.VoucherDate >= start && v.VoucherDate < endExclusive))
            from l in v.Lines
            where !l.IsDeleted
               && (l.Type == ProcessType.Cash || l.PaymentType == ProcessPaymentType.WithCash)
               && (filter.CashId == null || l.CommodityId == filter.CashId || l.PayCommodityId == filter.CashId)
            select new
            {
                v.VoucherDate, v.VoucherNumber, v.VaultId, v.SubAccountId,
                l.Type, l.PaymentType, l.Direction,
                l.MainUnitId, l.CommodityCode, l.Total,
                l.PayUnitId, l.PayCommodityCode, l.PayTotal,
                l.Description, l.CreationTime, l.Id,
            });

        var legs = new List<CashLeg>(rows.Count);
        foreach (var r in rows)
        {
            var inflow = ((int)r.Direction % 2) == 0;

            // Sol bacak: yalnız Cash process'te nakit. Giriş + / Çıkış −.
            if (r.Type == ProcessType.Cash && r.MainUnitId != Guid.Empty && r.Total != 0m)
                legs.Add(new CashLeg(r.MainUnitId, inflow ? r.Total : -r.Total, r.CommodityCode, "Nakit",
                    r.VoucherDate, r.VoucherNumber, r.Type, r.Direction, r.VaultId, r.SubAccountId, r.Description, r.CreationTime, r.Id));

            // Sağ bacak: Peşin (WithCash) olan TÜM process'lerde karşılık nakit (Cash+Peşin'de iki bacak da nakit →
            // biri girer biri çıkar, ör. döviz bozdurma). Mal Çıkış→nakit girer(+), mal Giriş→nakit çıkar(−).
            if (r.PaymentType == ProcessPaymentType.WithCash && r.PayUnitId is { } payUnit && r.PayTotal != 0m)
                legs.Add(new CashLeg(payUnit, inflow ? -r.PayTotal : r.PayTotal, r.PayCommodityCode, "Peşin",
                    r.VoucherDate, r.VoucherNumber, r.Type, r.Direction, r.VaultId, r.SubAccountId, r.Description, r.CreationTime, r.Id));
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
