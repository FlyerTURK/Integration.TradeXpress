using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Per-scope alış/satış marjı — <b>append-only</b> (CRUD değil). <b>Standart IMultiTenant</b>:
/// her tenant (host=null dahil) yalnız kendi marj satırlarını görür/yazar (paylaşım yok).
/// Marj değişimi = YENİ satır; güncel = (CurrencyUnitId) için en son <c>CreationTime</c>.
/// Birim Code/Name global <see cref="CurrencyUnit"/>'ten zenginleştirilir (filter disable ile).
///
/// <para>Satır sayısı küçük (birim × geçmiş) olduğundan filtre/sıralama/sayfalama projeksiyon
/// üzerinde bellek-içi yapılır → birim Code'una göre arama mümkün.</para>
/// </summary>
[Authorize(TradeXpressPermissions.CurrencyUnitMargins.Default)]
public class CurrencyUnitMarginAppService : TradeXpressAppService, ICurrencyUnitMarginAppService
{
    private readonly IRepository<CurrencyUnitMargin, Guid> _repository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IDataFilter _dataFilter;

    public CurrencyUnitMarginAppService(
        IRepository<CurrencyUnitMargin, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IDataFilter dataFilter)
    {
        _repository = repository;
        _unitRepository = unitRepository;
        _dataFilter = dataFilter;
    }

    public virtual async Task<PagedResultDto<CurrencyUnitMarginListDto>> GetListAsync(CurrencyUnitMarginListRequestDto input)
    {
        // Tenant filtresi AÇIK → yalnız bu scope'un (host=null) marjları.
        // Birim başına GÜNCEL satır = en son CreationTime (eşitlikte Id tie-break).
        // Veri küçük (birim × geçmiş) → grup bellek-içi; CreationTime ties'a dayanıklı.
        var all = await AsyncExecuter.ToListAsync(await _repository.GetQueryableAsync());
        var latest = all
            .GroupBy(m => m.CurrencyUnitId)
            .Select(g => g.OrderByDescending(x => x.CreationTime).ThenByDescending(x => x.Id).First())
            .ToList();

        var unitMap = await LoadUnitMapAsync(latest.Select(r => r.CurrencyUnitId));

        var projected = latest.Select(r => ToDto(r, unitMap)).AsQueryable();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim();
            projected = projected.Where(d =>
                d.CurrencyUnitCode.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                d.CurrencyUnitName.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = projected.OrderBy(d => d.DisplayOrder).ThenBy(d => d.CurrencyUnitCode).ToList();
        var page = ordered.Skip(input.SkipCount).Take(input.MaxResultCount).ToList();

        return new PagedResultDto<CurrencyUnitMarginListDto>(ordered.Count, page);
    }

    [Authorize(TradeXpressPermissions.CurrencyUnitMargins.Create)]
    public virtual async Task<CurrencyUnitMarginListDto> SetAsync(CurrencyUnitMarginSetDto input)
    {
        await EnsureUnitVisibleAsync(input.CurrencyUnitId);

        // Append-only: var olanı kontrol etme/düzeltme — her zaman YENİ satır.
        var entity = new CurrencyUnitMargin(
            GuidGenerator.Create(),
            input.CurrencyUnitId,
            new MarginSetting(input.MarginOnBuyType, input.MarginOnBuyValue),
            new MarginSetting(input.MarginOnSellType, input.MarginOnSellValue));

        await _repository.InsertAsync(entity, autoSave: true);

        var unitMap = await LoadUnitMapAsync(new[] { entity.CurrencyUnitId });
        return ToDto(entity, unitMap);
    }

    public virtual async Task<CurrencyUnitMarginListDto> GetCurrentAsync(Guid currencyUnitId)
    {
        await EnsureUnitVisibleAsync(currencyUnitId);

        var mq = await _repository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(mq.Where(m => m.CurrencyUnitId == currencyUnitId));
        var latest = rows.OrderByDescending(x => x.CreationTime).ThenByDescending(x => x.Id).FirstOrDefault();

        var unitMap = await LoadUnitMapAsync(new[] { currencyUnitId });
        if (latest is not null)
            return ToDto(latest, unitMap);

        // Henüz marj yok → varsayılan Passthrough (Multiply 1) göster.
        unitMap.TryGetValue(currencyUnitId, out var unit);
        return new CurrencyUnitMarginListDto
        {
            CurrencyUnitId = currencyUnitId,
            CurrencyUnitCode = unit?.Code ?? string.Empty,
            CurrencyUnitName = unit?.Name ?? string.Empty,
            UnitType = unit?.Type ?? CurrencyUnitType.Cash,
            DisplayOrder = unit?.DisplayOrder ?? 0,
            IsGlobalUnit = unit?.TenantId == null,
            MarginOnBuyType = MarginType.Multiply,
            MarginOnBuyValue = 1m,
            MarginOnSellType = MarginType.Multiply,
            MarginOnSellValue = 1m,
        };
    }

    public virtual async Task<List<CurrencyUnitMarginListDto>> GetHistoryAsync(Guid currencyUnitId)
    {
        await EnsureUnitVisibleAsync(currencyUnitId);

        var mq = await _repository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            mq.Where(m => m.CurrencyUnitId == currencyUnitId).OrderByDescending(m => m.CreationTime));

        var unitMap = await LoadUnitMapAsync(new[] { currencyUnitId });
        return rows.Select(r => ToDto(r, unitMap)).ToList();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task EnsureUnitVisibleAsync(Guid unitId)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var query = (await _unitRepository.GetQueryableAsync())
                .Where(u => u.Id == unitId && (u.TenantId == null || u.TenantId == tenantId));
            if (!await AsyncExecuter.AnyAsync(query))
                throw new EntityNotFoundException(typeof(CurrencyUnit), unitId);
        }
    }

    /// <summary>İlgili birimlerin kimliğini global+own scope'tan okur (filter disable).</summary>
    private async Task<Dictionary<Guid, CurrencyUnit>> LoadUnitMapAsync(IEnumerable<Guid> unitIds)
    {
        var ids = unitIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, CurrencyUnit>();

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var query = (await _unitRepository.GetQueryableAsync()).Where(u => ids.Contains(u.Id));
            var units = await AsyncExecuter.ToListAsync(query);
            return units.ToDictionary(u => u.Id);
        }
    }

    private static CurrencyUnitMarginListDto ToDto(CurrencyUnitMargin m, Dictionary<Guid, CurrencyUnit> unitMap)
    {
        unitMap.TryGetValue(m.CurrencyUnitId, out var unit);
        return new CurrencyUnitMarginListDto
        {
            Id = m.Id,
            CurrencyUnitId = m.CurrencyUnitId,
            CurrencyUnitCode = unit?.Code ?? string.Empty,
            CurrencyUnitName = unit?.Name ?? string.Empty,
            UnitType = unit?.Type ?? CurrencyUnitType.Cash,
            DisplayOrder = unit?.DisplayOrder ?? 0,
            IsGlobalUnit = unit?.TenantId == null,
            MarginOnBuyType = m.MarginOnBuy.Type,
            MarginOnBuyValue = m.MarginOnBuy.Value,
            MarginOnSellType = m.MarginOnSell.Type,
            MarginOnSellValue = m.MarginOnSell.Value,
            CreationTime = m.CreationTime,
        };
    }
}
