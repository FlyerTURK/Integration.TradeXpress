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

namespace Integration.TradeXpress.Futures;

/// <summary>
/// Future (Vadeli) CRUD. Görünürlük (Cash gibi): host kataloğu (TenantId=null) + tenant kendi kayıtları.
/// FollowingUnit ZORUNLU; FollowingFactor &gt;0. Tenant global kaydı düzenleyemez/silemez.
/// </summary>
[Authorize]
public class FutureAppService : TradeXpressAppService, IFutureAppService
{
    private readonly IRepository<Future, Guid> _repository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IDataFilter _dataFilter;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public FutureAppService(
        IRepository<Future, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IDataFilter dataFilter)
    {
        _repository     = repository;
        _unitRepository = unitRepository;
        _dataFilter     = dataFilter;
    }

    public virtual async Task<PagedResultDto<FutureListDto>> GetListAsync(FutureListRequestDto input)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var filtered = (await _repository.GetQueryableAsync())
                .Where(x => x.TenantId == null || x.TenantId == tenantId)
                .ApplyListRequest(input, AllowedListFields);

            // Future katalogu küçük → filtrelenmiş kümeyi materyalize edip composite default sırada sayfala.
            var all = await AsyncExecuter.ToListAsync(filtered);
            var totalCount = all.Count;

            var orders = await GetUnitOrdersAsync(all.Select(f => f.FollowingUnitId));

            // Grid listesi: kolon sıralaması yoksa düz Code artan (combo composite sırayı GetPickerList tutar).
            var explicitSort = (input.Sorts is { Count: > 0 }) || !string.IsNullOrWhiteSpace(input.Sorting);
            var ordered = explicitSort ? all : all.OrderBy(f => f.Code, StringComparer.OrdinalIgnoreCase).ToList();

            var dtos = ordered.Skip(input.SkipCount).Take(input.MaxResultCount).Select(MapList).ToList();
            ApplyUnitCodes(dtos, orders);
            return new PagedResultDto<FutureListDto>(totalCount, dtos);
        }
    }

    public virtual async Task<FutureGetDto> GetAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        var dto = MapGet(entity);
        dto.FollowingUnitCode = await ResolveUnitCodeAsync(entity.FollowingUnitId);
        return dto;
    }

    public virtual async Task<FutureGetDto> CreateAsync(FutureCreateDto input)
    {
        var entity = new Future(input.Code, input.Name, input.FollowingUnitId!.Value, input.FollowingFactor);
        entity.SetDescription(input.Description);

        await _repository.InsertAsync(entity, autoSave: true);
        return MapGet(entity);
    }

    public virtual async Task<FutureGetDto> UpdateAsync(Guid id, FutureUpdateDto input)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity);

        entity.SetName(input.Name);
        entity.SetFollowingUnit(input.FollowingUnitId!.Value);
        entity.SetFollowingFactor(input.FollowingFactor);
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

    public virtual async Task<List<FutureListDto>> GetPickerListAsync()
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var rows = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.TenantId == null || x.TenantId == tenantId));

            var orders = await GetUnitOrdersAsync(rows.Select(f => f.FollowingUnitId));
            var dtos = OrderComposite(rows, orders).Select(MapList).ToList();
            ApplyUnitCodes(dtos, orders);
            return dtos;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>FollowingUnit'in sıralama bilgisi: (GlobalRank: host=0, AlwaysShowInBalance, DisplayOrder, Code).</summary>
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

    // CurrencyUnit app service düzeni (global önce → AlwaysShowInBalance desc → DisplayOrder asc → Code asc)
    // + FollowingFactor desc + Future.Code asc.
    private static List<Future> OrderComposite(
        IEnumerable<Future> futures,
        IReadOnlyDictionary<Guid, (int Global, bool AlwaysShow, int DisplayOrder, string Code)> orders)
    {
        (int Global, bool AlwaysShow, int DisplayOrder, string Code) Key(Future f) =>
            orders.TryGetValue(f.FollowingUnitId, out var v) ? v : (int.MaxValue, false, int.MaxValue, string.Empty);

        return futures
            .OrderBy(f => Key(f).Global)
            .ThenByDescending(f => Key(f).AlwaysShow)
            .ThenBy(f => Key(f).DisplayOrder)
            .ThenBy(f => Key(f).Code, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(f => f.FollowingFactor)
            .ThenBy(f => f.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ApplyUnitCodes(
        List<FutureListDto> dtos,
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

    private async Task<Future> GetInScopeAsync(Guid id)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var entity = await AsyncExecuter.FirstOrDefaultAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId)));
            return entity ?? throw new EntityNotFoundException(typeof(Future), id);
        }
    }

    private void EnsureEditable(Future entity, bool isDelete = false)
    {
        if (entity.TenantId == null && CurrentTenant.Id != null)
        {
            throw new BusinessException(isDelete
                ? "TradeXpress:Future:CannotDeleteGlobalAsTenant"
                : "TradeXpress:Future:CannotEditGlobalAsTenant");
        }
    }

    // Mapperly + IsGlobal enrichment (FollowingUnitCode ayrıca ResolveUnitCode/ApplyUnitCodes). Instance → net'i tetiklemez.
    private FutureListDto MapList(Future f)
    {
        var dto = ObjectMapper.Map<Future, FutureListDto>(f);
        dto.IsGlobal = f.TenantId == null;
        return dto;
    }

    private FutureGetDto MapGet(Future f)
    {
        var dto = ObjectMapper.Map<Future, FutureGetDto>(f);
        dto.IsGlobal = f.TenantId == null;
        return dto;
    }
}
