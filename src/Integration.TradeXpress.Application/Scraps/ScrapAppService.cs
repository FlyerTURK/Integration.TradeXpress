using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Scraps;

/// <summary>
/// Scrap (Hurda) CRUD. Görünürlük (Future gibi): host kataloğu (TenantId=null) + tenant kendi kayıtları.
/// FollowingUnit ZORUNLU; Factor 0..1. Tenant global kaydı düzenleyemez/silemez. Sıralama: birim düzeni
/// (CurrencyUnit app service) → Factor desc → Code asc.
/// </summary>
[Authorize]
public class ScrapAppService : TradeXpressAppService, IScrapAppService
{
    private readonly IRepository<Scrap, Guid> _repository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IDataFilter _dataFilter;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public ScrapAppService(
        IRepository<Scrap, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IDataFilter dataFilter)
    {
        _repository     = repository;
        _unitRepository = unitRepository;
        _dataFilter     = dataFilter;
    }

    public virtual async Task<PagedResultDto<ScrapListDto>> GetListAsync(ScrapListRequestDto input)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var filtered = (await _repository.GetQueryableAsync())
                .Where(x => x.TenantId == null || x.TenantId == tenantId)
                .ApplyListRequest(input, AllowedListFields);

            var all = await AsyncExecuter.ToListAsync(filtered);
            var totalCount = all.Count;

            var orders = await GetUnitOrdersAsync(all.Select(s => s.FollowingUnitId));
            // Grid listesi: kolon sıralaması yoksa düz Code artan (combo composite sırayı GetPickerList tutar).
            var explicitSort = (input.Sorts is { Count: > 0 }) || !string.IsNullOrWhiteSpace(input.Sorting);
            var ordered = explicitSort ? all : all.OrderBy(s => s.Code, StringComparer.OrdinalIgnoreCase).ToList();

            var dtos = ordered.Skip(input.SkipCount).Take(input.MaxResultCount).Select(MapList).ToList();
            ApplyUnitCodes(dtos, orders);
            return new PagedResultDto<ScrapListDto>(totalCount, dtos);
        }
    }

    public virtual async Task<ScrapGetDto> GetAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        var dto = MapGet(entity);
        dto.FollowingUnitCode = await ResolveUnitCodeAsync(entity.FollowingUnitId);
        return dto;
    }

    public virtual async Task<ScrapGetDto> CreateAsync(ScrapCreateDto input)
    {
        var entity = new Scrap(input.Code, input.Name, input.FollowingUnitId!.Value, input.Factor, input.FactorChange);
        entity.SetDescription(input.Description);

        await _repository.InsertAsync(entity, autoSave: true);
        return MapGet(entity);
    }

    public virtual async Task<ScrapGetDto> UpdateAsync(Guid id, ScrapUpdateDto input)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity);

        entity.SetName(input.Name);
        entity.SetFollowingUnit(input.FollowingUnitId!.Value);
        entity.SetFactor(input.Factor);
        entity.SetFactorChange(input.FactorChange);
        entity.SetDescription(input.Description);
        entity.SetActive(input.IsActive);

        await _repository.UpdateAsync(entity, autoSave: true);
        return MapGet(entity);
    }

    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity, isDelete: true);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    public virtual async Task<List<ScrapListDto>> GetPickerListAsync()
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var rows = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.TenantId == null || x.TenantId == tenantId));

            var orders = await GetUnitOrdersAsync(rows.Select(s => s.FollowingUnitId));
            var dtos = OrderComposite(rows, orders).Select(MapList).ToList();
            ApplyUnitCodes(dtos, orders);
            return dtos;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, (int Global, bool AlwaysShow, int DisplayOrder, string Code)>>
        GetUnitOrdersAsync(IEnumerable<Guid> unitIds)
    {
        var ids = unitIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return new();

        using (_dataFilter.Disable<IMultiTenant>())
        {
            return (await AsyncExecuter.ToListAsync(
                    (await _unitRepository.GetQueryableAsync())
                        .Where(u => ids.Contains(u.Id))
                        .Select(u => new { u.Id, u.TenantId, u.AlwaysShowInBalance, u.DisplayOrder, u.Code })))
                .ToDictionary(
                    u => u.Id,
                    u => (u.TenantId == null ? 0 : 1, u.AlwaysShowInBalance, u.DisplayOrder, u.Code ?? string.Empty));
        }
    }

    // Birim düzeni (global önce → AlwaysShowInBalance desc → DisplayOrder asc → Code asc) + Factor desc + Code asc.
    private static List<Scrap> OrderComposite(
        IEnumerable<Scrap> scraps,
        IReadOnlyDictionary<Guid, (int Global, bool AlwaysShow, int DisplayOrder, string Code)> orders)
    {
        (int Global, bool AlwaysShow, int DisplayOrder, string Code) Key(Scrap s) =>
            orders.TryGetValue(s.FollowingUnitId, out var v) ? v : (int.MaxValue, false, int.MaxValue, string.Empty);

        return scraps
            .OrderBy(s => Key(s).Global)
            .ThenByDescending(s => Key(s).AlwaysShow)
            .ThenBy(s => Key(s).DisplayOrder)
            .ThenBy(s => Key(s).Code, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(s => s.Factor)
            .ThenBy(s => s.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ApplyUnitCodes(
        List<ScrapListDto> dtos,
        IReadOnlyDictionary<Guid, (int Global, bool AlwaysShow, int DisplayOrder, string Code)> orders)
    {
        foreach (var d in dtos)
            if (orders.TryGetValue(d.FollowingUnitId, out var v))
                d.FollowingUnitCode = v.Code;
    }

    private async Task<string?> ResolveUnitCodeAsync(Guid unitId)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            return await AsyncExecuter.FirstOrDefaultAsync(
                (await _unitRepository.GetQueryableAsync()).Where(u => u.Id == unitId).Select(u => u.Code));
        }
    }

    private async Task<Scrap> GetInScopeAsync(Guid id)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var entity = await AsyncExecuter.FirstOrDefaultAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId)));
            return entity ?? throw new EntityNotFoundException(typeof(Scrap), id);
        }
    }

    private void EnsureEditable(Scrap entity, bool isDelete = false)
    {
        if (entity.TenantId == null && CurrentTenant.Id != null)
        {
            throw new BusinessException(isDelete
                ? "TradeXpress:Scrap:CannotDeleteGlobalAsTenant"
                : "TradeXpress:Scrap:CannotEditGlobalAsTenant");
        }
    }

    // Mapperly + IsGlobal enrichment (FollowingUnitCode ayrıca ApplyUnitCodes ile). Instance → statik değil, net'i tetiklemez.
    private ScrapListDto MapList(Scrap s)
    {
        var dto = ObjectMapper.Map<Scrap, ScrapListDto>(s);
        dto.IsGlobal = s.TenantId == null;
        return dto;
    }

    private ScrapGetDto MapGet(Scrap s)
    {
        var dto = ObjectMapper.Map<Scrap, ScrapGetDto>(s);
        dto.IsGlobal = s.TenantId == null;
        return dto;
    }
}
