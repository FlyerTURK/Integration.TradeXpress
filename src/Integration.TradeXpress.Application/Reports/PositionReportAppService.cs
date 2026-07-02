using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vouchers.Balance;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Pozisyon raporu. Kalıcı <see cref="BalanceLedgerEntry"/>'i (poster çıktısı) scope'la GROUP BY UnitId + SUM
/// ile birim-net'e indirir (tek LINQ, SUM DB-tarafı), bilanço birimine re-base'li değerler, base-dışı net
/// açığı DURUM olarak toplar. Kural HARDCODE değil — net tamamen ledger'dan (= canlı poster davranışı).
/// İşaret: + alacak/long, − borç/short. Base satır görünür ama DURUM dışı (kendine karşı risk yok).
/// </summary>
[Authorize(TradeXpressPermissions.Reports.Position)]
public class PositionReportAppService : TradeXpressAppService, IPositionReportAppService
{
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledgerRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IEffectivePriceAppService _effectivePriceAppService;
    private readonly ICurrentCompany _currentCompany;

    public PositionReportAppService(
        IRepository<BalanceLedgerEntry, Guid> ledgerRepository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Company, Guid> companyRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IEffectivePriceAppService effectivePriceAppService,
        ICurrentCompany currentCompany)
    {
        _ledgerRepository         = ledgerRepository;
        _branchRepository         = branchRepository;
        _companyRepository        = companyRepository;
        _unitRepository           = unitRepository;
        _effectivePriceAppService = effectivePriceAppService;
        _currentCompany           = currentCompany;
    }

    public virtual async Task<PositionReportResultDto> GetAsync(PositionReportFilterDto filter)
    {
        // SIZINTI ÖNLEME: rapor DAİMA çalışılan (working) şirketle sınırlı. Client'ın CompanyId'si GÜVENİLMEZ →
        // ambient ICurrentCompany (working-context köprüsü) ile EZİLİR. Çalışılan şirket yoksa (host/API) boş.
        if (_currentCompany.Id is not { } companyId)
            return new PositionReportResultDto();

        // Branch: client working şubeyi gönderir; çalışılan şirkete AİT DEĞİLSE düşür (cross-company forge koruması).
        Guid? branchId = filter.BranchId;
        if (branchId is { } bid)
        {
            var branch = await _branchRepository.FindAsync(bid);
            if (branch is null || branch.CompanyId != companyId)
                branchId = null;
        }

        // ① Bilanço (base) birimi: working şube base'i (boşsa şirket base'i).
        var baseUnitId = await ResolveBaseUnitAsync(companyId, branchId);

        // ② Ledger'ı working scope'la GROUP BY UnitId + SUM — tek LINQ (SUM DB-tarafı). Ledger IMultiTenant →
        //    otomatik tenant filtresi; şirket ambient'ten zorlanır → cross-company sızıntı imkânsız.
        var q = await _ledgerRepository.GetQueryableAsync();
        var nets = await AsyncExecuter.ToListAsync(
            from e in q
            where e.CompanyId == companyId
               && (branchId == null || e.BranchId == branchId)
            group e by e.UnitId into g
            select new { UnitId = g.Key, Net = g.Sum(x => x.Amount) });

        // FİRMA perspektifi (bilanço ile TUTARLI): ledger HESAP bakiyesini saklar (müşteri borçlanır → −). Firmanın net
        // pozisyonu = −Σ — müşteri borcu = bizim ALACAĞIMIZ/varlığımız (+), müşteri alacağı = bizim borcumuz (−). Tek
        // yerden çevir → tüm satır (NetAmount/Valued) + DURUM tutarlı. (Kullanıcı: "hizmet çıkış = müşteri borçlanır".)
        nets = nets.Select(x => new { x.UnitId, Net = -x.Net }).ToList();

        var result = new PositionReportResultDto { BaseUnitId = baseUnitId };

        if (nets.Count == 0)
        {
            result.BaseCurrencyCode = await UnitCodeAsync(baseUnitId);
            return result;
        }

        // ③ Değerleme: base birime re-base'li efektifler (şube base'i şirket base'inden farklı olabilir).
        var valuation = (await _effectivePriceAppService.GetValuationByBaseAsync(baseUnitId))
            .ToDictionary(v => v.Id);

        var unitCodes = await UnitCodesAsync(nets.Select(n => n.UnitId).Append(baseUnitId));
        result.BaseCurrencyCode = unitCodes.GetValueOrDefault(baseUnitId) ?? string.Empty;

        // ④ Satırlar + ⑤ DURUM (base-dışı değerlenmiş net açık toplamı).
        foreach (var n in nets)
        {
            var isBase = n.UnitId == baseUnitId;
            valuation.TryGetValue(n.UnitId, out var val);

            var row = new PositionRowDto
            {
                UnitId           = n.UnitId,
                UnitCode         = unitCodes.GetValueOrDefault(n.UnitId),
                NetAmount        = n.Net,
                IsBaseUnit       = isBase,
                CountsInPosition = !isBase,
                MissingRate      = val == null,
                ValuedBuy        = val == null ? 0m : n.Net * val.Buy,
                ValuedSell       = val == null ? 0m : n.Net * val.Sell,
            };
            result.Rows.Add(row);

            if (!isBase && val != null)
            {
                result.DurumBuy  += row.ValuedBuy;
                result.DurumSell += row.ValuedSell;
            }
        }

        // Base satır en üstte, sonra kod sırası.
        result.Rows = result.Rows
            .OrderByDescending(r => r.IsBaseUnit)
            .ThenBy(r => r.UnitCode)
            .ToList();

        return result;
    }

    /// <summary>Bilanço birimi: working şube base'i (boş Guid ise şirket base'ine düşer); şube yoksa şirket base'i.</summary>
    private async Task<Guid> ResolveBaseUnitAsync(Guid companyId, Guid? branchId)
    {
        if (branchId is { } bid)
        {
            var branch = await _branchRepository.FindAsync(bid);
            if (branch != null && branch.BaseCurrencyUnitId != Guid.Empty)
                return branch.BaseCurrencyUnitId;
        }

        var company = await _companyRepository.FindAsync(companyId);
        return company?.BaseCurrencyUnitId ?? Guid.Empty;
    }

    /// <summary>Birim kodları (CurrencyUnit global → tenant filtresi kapalı, host‖own).</summary>
    private async Task<Dictionary<Guid, string>> UnitCodesAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (idList.Count == 0) return new();

        using (DataFilter.Disable<IMultiTenant>())
        {
            var rows = await AsyncExecuter.ToListAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => idList.Contains(u.Id))
                    .Select(u => new { u.Id, u.Code }));
            return rows.ToDictionary(x => x.Id, x => x.Code);
        }
    }

    private async Task<string> UnitCodeAsync(Guid id)
        => (await UnitCodesAsync(new[] { id })).GetValueOrDefault(id) ?? string.Empty;
}
