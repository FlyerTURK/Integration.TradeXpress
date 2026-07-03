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

    /// <summary>Hesabın (opsiyonel tarihe kadar) birim-bazlı net bakiyesi + bakiye para birimi.</summary>
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

        // Hesabın bakiye para birimi (konsolide hedefi): SubAccount → Account → BalanceCurrencyUnit.
        var (baseUnitId, baseCode) = await _codeResolver.ResolveBalanceUnitAsync(subAccountId);

        return new AccountBalanceDto
        {
            BalanceUnitId = baseUnitId,
            BalanceCode   = baseCode,
            Lines         = rows,
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
