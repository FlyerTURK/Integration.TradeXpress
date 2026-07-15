using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Vouchers.Balance;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Hesap ekstresi + bakiye okuma servisi (legacy <c>Cari.HesapExtresiEx</c> paritesi). Company scope
/// parametreyle gelir — guard (working-context zorlaması) çağıran AppService'te kalır. Yürüyen bakiye
/// sıralamaya duyarlıdır: dönem satırları VoucherDate → CreationTime → Id sırasıyla işlenir.
/// </summary>
public class VoucherStatementService : ITransientDependency
{
    private readonly IRepository<Voucher, Guid> _repository;
    private readonly VoucherBalanceCalculator _balanceCalculator;
    private readonly VoucherCodeResolver _codeResolver;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public VoucherStatementService(
        IRepository<Voucher, Guid> repository,
        VoucherBalanceCalculator balanceCalculator,
        VoucherCodeResolver codeResolver,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _repository        = repository;
        _balanceCalculator = balanceCalculator;
        _codeResolver      = codeResolver;
        _asyncExecuter     = asyncExecuter;
    }

    /// <summary>Hesap ekstresi: dönem satırları + devreden + kapanış. <paramref name="types"/> doluysa
    /// hem dönem satırları hem devreden AYNI tip filtresiyle hesaplanır — filtreli ekstrenin yürüyen
    /// bakiyesi kendi içinde tutarlı kalır (filtresiz çağrıda dip-toplam = Bakiye sekmesi).</summary>
    public async Task<AccountStatementDto> GetAccountStatementAsync(
        Guid companyId, Guid subAccountId, DateTime start, DateTime endExclusive, List<ProcessType>? types = null)
    {
        var typeFilter = types is { Count: > 0 } ? types : null;   // boş liste = filtre yok

        var q = await _repository.GetQueryableAsync();
        var rangeQuery =
            from v in q
            where v.CompanyId == companyId && v.SubAccountId == subAccountId && v.VoucherDate >= start && v.VoucherDate < endExclusive
            from l in v.Lines
            where !l.IsDeleted
            select new { Line = l, v.VoucherDate, v.VoucherNumber };
        if (typeFilter != null)
        {
            rangeQuery = rangeQuery.Where(r => typeFilter.Contains(r.Line.Type));
        }
        var rows = await _asyncExecuter.ToListAsync(rangeQuery);

        var ordered = rows
            .OrderBy(r => r.VoucherDate).ThenBy(r => r.Line.CreationTime).ThenBy(r => r.Line.Id)
            .ToList();
        var displayed = ordered.Select(r => r.Line).ToList();

        var dtos = displayed.Select(VoucherLineDtoFactory.MapLine).ToList();
        for (var i = 0; i < dtos.Count; i++)
        {
            dtos[i].VoucherDate   = ordered[i].VoucherDate;
            dtos[i].VoucherNumber = ordered[i].VoucherNumber;
        }

        await _codeResolver.ResolveUnitCodesAsync(dtos);
        await _codeResolver.ResolveCounterAccountCodesAsync(dtos);
        await _codeResolver.ResolveCreatorNamesAsync(dtos);

        // Devreden: start'tan önceki satırların (aynı tip filtresiyle) birim-bazlı neti.
        // Dönemde satır olmasa da hesaplanır — boş dönemde bile devreden görünür kalmalı.
        var carryQuery = (await _repository.GetQueryableAsync())
            .Where(v => v.CompanyId == companyId && v.SubAccountId == subAccountId && v.VoucherDate < start)
            .SelectMany(v => v.Lines)
            .Where(l => !l.IsDeleted);
        if (typeFilter != null)
        {
            carryQuery = carryQuery.Where(l => typeFilter.Contains(l.Type));
        }
        var carryLines = await _asyncExecuter.ToListAsync(carryQuery);

        if (displayed.Count > 0)
        {
            await AssignRunningBalancesAsync(displayed, dtos, carryLines);
        }

        var opening = await ToBalanceRowsAsync(_balanceCalculator.Aggregate(carryLines));
        var closing = dtos.Count > 0 ? dtos[^1].RunningBalances : opening;

        return new AccountStatementDto
        {
            OpeningBalances = opening,
            Lines           = dtos,
            ClosingBalances = closing,
        };
    }

    /// <summary>Karşı tarafın (opsiyonel tarihe kadar) birim-bazlı net bakiyesi + bakiye para birimi.
    /// <para><b>Tip-agnostik:</b> <paramref name="subAccountId"/> cari kipinde SubAccount, kasa kipinde
    /// KASA id'sidir (Voucher.SubAccountId polimorfiktir) → kasa bakiyeleri sahte cari olmadan, bu sorgu
    /// hiç değişmeden ayrışır.</para></summary>
    public async Task<AccountBalanceDto> GetBalancesAsync(Guid companyId, Guid subAccountId, DateTime? upTo = null)
    {
        var q = (await _repository.GetQueryableAsync())
            .Where(v => v.CompanyId == companyId && v.SubAccountId == subAccountId);
        if (upTo.HasValue)
        {
            q = q.Where(v => v.VoucherDate <= upTo.Value);
        }

        var lines = await _asyncExecuter.ToListAsync(
            q.SelectMany(v => v.Lines).Where(l => !l.IsDeleted));

        var net  = _balanceCalculator.Aggregate(lines);   // UnitId → işaretli net
        var rows = await ToBalanceRowsAsync(net);

        // Karşı tarafın TİPİ veriden okunur (id polimorfik: cari kipinde SubAccount, kasa kipinde Kasa) —
        // bakiye biriminin kaynağı tipe göre değişir. Hiç fiş yoksa cari kabul edilir (bugünkü davranış).
        var accountType = await _asyncExecuter.FirstOrDefaultAsync(
            q.Select(v => (AccountType?)v.AccountType)) ?? AccountType.CurrentAccount;
        var (baseUnitId, baseCode) = await _codeResolver.ResolveBalanceUnitAsync(companyId, accountType, subAccountId);

        return new AccountBalanceDto
        {
            BalanceUnitId = baseUnitId,
            BalanceCode   = baseCode,
            Lines         = rows,
        };
    }

    /// <summary>Bakiye Gösterim Modu = AccountScoped: <paramref name="accountId"/>'nin (cari kipte Account,
    /// iç kipte Şube) TÜM alt hesaplarının/kasalarının KONSOLİDE net bakiyesi — seçili tek alt hesap/kasa değil.
    /// <see cref="GetBalancesAsync(Guid,Guid,DateTime?)"/> ile aynı hesap mantığı, filtre yalnız
    /// <c>AccountId</c> üzerinden (SubAccountId yok).</summary>
    public async Task<AccountBalanceDto> GetAccountScopedBalancesAsync(Guid companyId, Guid accountId, DateTime? upTo = null)
    {
        var q = (await _repository.GetQueryableAsync())
            .Where(v => v.CompanyId == companyId && v.AccountId == accountId);
        if (upTo.HasValue)
        {
            q = q.Where(v => v.VoucherDate <= upTo.Value);
        }

        var lines = await _asyncExecuter.ToListAsync(
            q.SelectMany(v => v.Lines).Where(l => !l.IsDeleted));

        var net  = _balanceCalculator.Aggregate(lines);
        var rows = await ToBalanceRowsAsync(net);

        var accountType = await _asyncExecuter.FirstOrDefaultAsync(
            q.Select(v => (AccountType?)v.AccountType)) ?? AccountType.CurrentAccount;
        var (baseUnitId, baseCode) = await _codeResolver.ResolveBalanceUnitByAccountScopeAsync(companyId, accountType, accountId);

        return new AccountBalanceDto
        {
            BalanceUnitId = baseUnitId,
            BalanceCode   = baseCode,
            Lines         = rows,
        };
    }

    /// <summary>Bakiye sekmesinde bir birime çift-tıklayınca açılan tarihçe: seçili kapsamın (
    /// <paramref name="scopeIsAccount"/> false → SubAccount/Kasa id'si, true → Account/Şube id'si —
    /// Bakiye Gösterim Modu'yla aynı ayrım) [start, endExclusive) aralığında YALNIZ <paramref name="unitId"/>'yi
    /// etkileyen satırlar (Delta≠0), devreden + yürüyen net ile. Diğer birimleri etkileyen satırlar atlanır —
    /// "bu birimin tarihçesi" budur.</summary>
    public async Task<UnitStatementDto> GetUnitStatementAsync(
        Guid companyId, bool scopeIsAccount, Guid scopeId, Guid unitId, DateTime start, DateTime endExclusive)
    {
        var baseQuery = (await _repository.GetQueryableAsync()).Where(v => v.CompanyId == companyId);
        baseQuery = scopeIsAccount
            ? baseQuery.Where(v => v.AccountId == scopeId)
            : baseQuery.Where(v => v.SubAccountId == scopeId);

        var rangeQuery =
            from v in baseQuery
            where v.VoucherDate >= start && v.VoucherDate < endExclusive
            from l in v.Lines
            where !l.IsDeleted
            select new { Line = l, v.VoucherDate, v.VoucherNumber };
        var rows = await _asyncExecuter.ToListAsync(rangeQuery);

        var ordered = rows
            .OrderBy(r => r.VoucherDate).ThenBy(r => r.Line.CreationTime).ThenBy(r => r.Line.Id)
            .ToList();

        var carryLines = await _asyncExecuter.ToListAsync(
            baseQuery.Where(v => v.VoucherDate < start)
                .SelectMany(v => v.Lines)
                .Where(l => !l.IsDeleted));

        var opening = _balanceCalculator.Aggregate(carryLines).GetValueOrDefault(unitId);
        var running = opening;
        var lines = new List<UnitStatementLineDto>();

        foreach (var r in ordered)
        {
            var effect = _balanceCalculator.Post(r.Line).FirstOrDefault(e => e.UnitId == unitId);
            if (effect.UnitId != unitId)
            {
                continue;   // bu satır seçili birimi etkilemiyor → tarihçede görünmez
            }

            running += effect.Amount;

            var dto = VoucherLineDtoFactory.MapLine(r.Line);
            dto.VoucherDate   = r.VoucherDate;
            dto.VoucherNumber = r.VoucherNumber;

            lines.Add(new UnitStatementLineDto
            {
                Line       = dto,
                Delta      = effect.Amount,
                RunningNet = running,
            });
        }

        if (lines.Count > 0)
        {
            var dtos = lines.Select(l => l.Line).ToList();
            await _codeResolver.ResolveUnitCodesAsync(dtos);
            await _codeResolver.ResolveCounterAccountCodesAsync(dtos);
            await _codeResolver.ResolveCreatorNamesAsync(dtos);
        }

        var unitCode = await _codeResolver.ResolveUnitCodeAsync(unitId) ?? string.Empty;

        return new UnitStatementDto
        {
            UnitId     = unitId,
            UnitCode   = unitCode,
            OpeningNet = opening,
            Lines      = lines,
            ClosingNet = lines.Count > 0 ? lines[^1].RunningNet : opening,
        };
    }

    /// <summary>Devreden (<paramref name="carryLines"/>) + sıralı görüntülenen satırlardan her satıra
    /// kadarki yürüyen bakiyeyi (birim-bazlı) hesaplar ve <paramref name="dtos"/>'ya yazar.</summary>
    public async Task AssignRunningBalancesAsync(
        List<VoucherLine> displayed, List<VoucherLineDto> dtos, List<VoucherLine> carryLines)
    {
        if (displayed.Count == 0)
        {
            return;
        }

        var running = new Dictionary<Guid, decimal>(_balanceCalculator.Aggregate(carryLines));

        var ids = new HashSet<Guid>(running.Keys);
        foreach (var l in displayed)
        {
            foreach (var e in _balanceCalculator.Post(l))
            {
                ids.Add(e.UnitId);
            }
        }

        var orderedUnits = await _codeResolver.OrderedVisibleUnitsAsync(ids);

        for (var i = 0; i < displayed.Count; i++)
        {
            foreach (var e in _balanceCalculator.Post(displayed[i]))
            {
                running.TryGetValue(e.UnitId, out var cur);
                running[e.UnitId] = cur + e.Amount;
            }

            dtos[i].RunningBalances = orderedUnits
                .Select(u => new VoucherBalanceLineDto
                {
                    UnitId   = u.Id,
                    UnitCode = u.Code,
                    Net      = running.GetValueOrDefault(u.Id),
                })
                .ToList();
        }
    }

    /// <summary>Birim → net sözlüğünü görünür-birim sırasıyla bakiye satırlarına çevirir (ekstre devreden/kapanış + Bakiye sekmesi ortak yolu).</summary>
    public async Task<List<VoucherBalanceLineDto>> ToBalanceRowsAsync(IReadOnlyDictionary<Guid, decimal> net)
    {
        var ordered = await _codeResolver.OrderedVisibleUnitsAsync(net.Keys);
        return ordered
            .Select(u => new VoucherBalanceLineDto { UnitId = u.Id, UnitCode = u.Code, Net = net.GetValueOrDefault(u.Id) })
            .ToList();
    }
}
