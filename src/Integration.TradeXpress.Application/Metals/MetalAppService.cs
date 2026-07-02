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

namespace Integration.TradeXpress.Metals;

/// <summary>
/// Metal (Maden) CRUD. Görünürlük (Scrap gibi): host kataloğu (TenantId=null) + tenant kendi kayıtları.
/// FollowingUnit ZORUNLU; Factor &gt;0 (üst sınır yok). Tenant global kaydı düzenleyemez/silemez.
/// Grid: kolon sıralaması yoksa Code artan; picker: birim düzeni → Factor desc → Code asc.
/// </summary>
[Authorize]
public class MetalAppService : TradeXpressAppService, IMetalAppService
{
    private readonly IRepository<Metal, Guid> _repository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IDataFilter _dataFilter;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public MetalAppService(
        IRepository<Metal, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IDataFilter dataFilter)
    {
        _repository     = repository;
        _unitRepository = unitRepository;
        _dataFilter     = dataFilter;
    }

    public virtual async Task<PagedResultDto<MetalListDto>> GetListAsync(MetalListRequestDto input)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var filtered = (await _repository.GetQueryableAsync())
                .Where(x => x.TenantId == null || x.TenantId == tenantId)
                .ApplyListRequest(input, AllowedListFields);

            var all = await AsyncExecuter.ToListAsync(filtered);
            var totalCount = all.Count;

            var orders = await GetUnitOrdersAsync(all.Select(m => m.FollowingUnitId));
            var explicitSort = (input.Sorts is { Count: > 0 }) || !string.IsNullOrWhiteSpace(input.Sorting);
            var ordered = explicitSort ? all : all.OrderBy(m => m.Code, StringComparer.OrdinalIgnoreCase).ToList();

            var dtos = ordered.Skip(input.SkipCount).Take(input.MaxResultCount).Select(MapList).ToList();
            ApplyUnitCodes(dtos, orders);
            return new PagedResultDto<MetalListDto>(totalCount, dtos);
        }
    }

    public virtual async Task<MetalGetDto> GetAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        var dto = MapGet(entity);
        dto.FollowingUnitCode = await ResolveUnitCodeAsync(entity.FollowingUnitId);
        return dto;
    }

    public virtual async Task<MetalGetDto> CreateAsync(MetalCreateDto input)
    {
        var entity = new Metal(
            input.Code, input.Name, input.FollowingUnitId!.Value,
            input.Factor, input.FactorChange,
            input.IsQuantity, input.StableQuantity,
            input.LaborType, input.LaborTypeChange,
            input.EntryLabor, input.EntryLaborUnitId, input.EntryLaborChange,
            input.ExitLabor, input.ExitLaborUnitId, input.ExitLaborChange,
            input.CostUnitId);
        entity.SetBarcode(input.Barcode);
        entity.SetDescription(input.Description);

        await _repository.InsertAsync(entity, autoSave: true);
        return MapGet(entity);
    }

    public virtual async Task<MetalGetDto> UpdateAsync(Guid id, MetalUpdateDto input)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity);

        entity.SetName(input.Name);
        entity.SetFollowingUnit(input.FollowingUnitId!.Value);
        entity.SetFactor(input.Factor);
        entity.SetFactorChange(input.FactorChange);
        entity.SetQuantityTracking(input.IsQuantity, input.StableQuantity);
        entity.SetLabor(
            input.LaborType, input.LaborTypeChange,
            input.EntryLabor, input.EntryLaborUnitId, input.EntryLaborChange,
            input.ExitLabor, input.ExitLaborUnitId, input.ExitLaborChange,
            input.CostUnitId);
        entity.SetBarcode(input.Barcode);
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

    public virtual async Task<List<MetalListDto>> GetPickerListAsync()
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var rows = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.TenantId == null || x.TenantId == tenantId));

            var orders = await GetUnitOrdersAsync(rows.Select(m => m.FollowingUnitId));
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

    private static List<Metal> OrderComposite(
        IEnumerable<Metal> metals,
        IReadOnlyDictionary<Guid, (int Global, bool AlwaysShow, int DisplayOrder, string Code)> orders)
    {
        (int Global, bool AlwaysShow, int DisplayOrder, string Code) Key(Metal m) =>
            orders.TryGetValue(m.FollowingUnitId, out var v) ? v : (int.MaxValue, false, int.MaxValue, string.Empty);

        return metals
            .OrderBy(m => Key(m).Global)
            .ThenByDescending(m => Key(m).AlwaysShow)
            .ThenBy(m => Key(m).DisplayOrder)
            .ThenBy(m => Key(m).Code, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(m => m.Factor)
            .ThenBy(m => m.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ApplyUnitCodes(
        List<MetalListDto> dtos,
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

    private async Task<Metal> GetInScopeAsync(Guid id)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var entity = await AsyncExecuter.FirstOrDefaultAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId)));
            return entity ?? throw new EntityNotFoundException(typeof(Metal), id);
        }
    }

    private void EnsureEditable(Metal entity, bool isDelete = false)
    {
        if (entity.TenantId == null && CurrentTenant.Id != null)
        {
            throw new BusinessException(isDelete
                ? "TradeXpress:Metal:CannotDeleteGlobalAsTenant"
                : "TradeXpress:Metal:CannotEditGlobalAsTenant");
        }
    }

    // Mapperly + IsGlobal enrichment (FollowingUnitCode ayrıca ApplyUnitCodes/ResolveUnitCode ile). Instance → net'i tetiklemez.
    private MetalListDto MapList(Metal m)
    {
        var dto = ObjectMapper.Map<Metal, MetalListDto>(m);
        dto.IsGlobal = m.TenantId == null;
        return dto;
    }

    private MetalGetDto MapGet(Metal m)
    {
        var dto = ObjectMapper.Map<Metal, MetalGetDto>(m);
        dto.IsGlobal = m.TenantId == null;
        return dto;
    }
}
