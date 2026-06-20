using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// CurrencyUnit CRUD. <b>Görünürlük kuralı:</b> host kataloğu (TenantId=null) HERKESE
/// görünür + tenant kendi birimlerini görür. ABP'nin standart multi-tenant filter'ı
/// tenant'a yalnız kendi verisini gösterirdi (global kataloğu gizlerdi); bu yüzden
/// filter <b>disable</b> edilip açık predicate uygulanır: <c>TenantId == null || == CurrentTenant.Id</c>.
/// (Host'ta CurrentTenant.Id=null → predicate yalnız global'i verir; başka tenant görünmez.)
///
/// <para>Tenant, global (host) birimi düzenleyemez/silemez — yalnız okur. Kendi kâr marjını
/// ayrı <c>TenantCurrencyMargin</c> katmanında tutacak (sonraki increment).</para>
/// </summary>
[Authorize(TradeXpressPermissions.CurrencyUnits.Default)]
public class CurrencyUnitAppService : TradeXpressAppService, ICurrencyUnitAppService
{
    private readonly IRepository<CurrencyUnit, Guid> _repository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IDataFilter _dataFilter;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase)
        { "Code", "Name", "Type", "IsActive", "DisplayOrder", "Id" };

    public CurrencyUnitAppService(
        IRepository<CurrencyUnit, Guid> repository,
        IGuidGenerator guidGenerator,
        IDataFilter dataFilter)
    {
        _repository = repository;
        _guidGenerator = guidGenerator;
        _dataFilter = dataFilter;
    }

    public virtual async Task<PagedResultDto<CurrencyUnitListDto>> GetListAsync(CurrencyUnitListRequestDto input)
    {
        // Multi-tenant filter'ı kapat, görünürlüğü kendimiz belirle (global + kendi).
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var query = (await _repository.GetQueryableAsync())
                .Where(x => x.TenantId == null || x.TenantId == tenantId)
                .ApplyListRequest(input, AllowedListFields);

            var totalCount = await AsyncExecuter.CountAsync(query);
            var items = await AsyncExecuter.ToListAsync(
                query.Skip(input.SkipCount).Take(input.MaxResultCount));

            var parentIds = items.Where(x => x.FollowingUnitId.HasValue).Select(x => x.FollowingUnitId.Value).Distinct().ToList();
            var parents = parentIds.Count > 0 
                ? await AsyncExecuter.ToListAsync((await _repository.GetQueryableAsync()).Where(x => parentIds.Contains(x.Id)))
                : new List<CurrencyUnit>();

            return new PagedResultDto<CurrencyUnitListDto>(
                totalCount,
                items.Select(e => ToListDto(e, parents)).ToList());
        }
    }

    public virtual async Task<CurrencyUnitGetDto> GetAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        return ToGetDto(entity);
    }

    [Authorize(TradeXpressPermissions.CurrencyUnits.Create)]
    public virtual async Task<CurrencyUnitGetDto> CreateAsync(CurrencyUnitCreateDto input)
    {
        // TenantId otomatik atanır (ABP IMultiTenant): host→null (global), tenant→kendi.
        var entity = new CurrencyUnit(
            _guidGenerator.Create(),
            input.Code,
            input.Name,
            input.Type,
            isSystem: false,
            displayOrder: input.DisplayOrder);

        entity.SetDescription(input.Description);
        await ApplyFollowingAsync(entity, input.FollowingUnitId, input.FollowingMarginType, input.FollowingMarginValue);

        await _repository.InsertAsync(entity, autoSave: true);
        return ToGetDto(entity);
    }

    [Authorize(TradeXpressPermissions.CurrencyUnits.Update)]
    public virtual async Task<CurrencyUnitGetDto> UpdateAsync(Guid id, CurrencyUnitUpdateDto input)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity);

        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        entity.SetDisplayOrder(input.DisplayOrder);
        if (input.IsActive) entity.Activate(); else entity.Deactivate();

        await ApplyFollowingAsync(entity, input.FollowingUnitId, input.FollowingMarginType, input.FollowingMarginValue);

        await _repository.UpdateAsync(entity, autoSave: true);
        return ToGetDto(entity);
    }

    [Authorize(TradeXpressPermissions.CurrencyUnits.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity, isDelete: true);

        if (entity.IsSystem)
            throw new BusinessException("TradeXpress:CurrencyUnit:CannotDeleteSystem");

        await _repository.DeleteAsync(entity, autoSave: true);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Id'yi görünürlük scope'unda (global + kendi) çeker; yoksa EntityNotFound.</summary>
    private async Task<CurrencyUnit> GetInScopeAsync(Guid id)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var query = (await _repository.GetQueryableAsync())
                .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId));

            var entity = await AsyncExecuter.FirstOrDefaultAsync(query);
            if (entity is null)
                throw new EntityNotFoundException(typeof(CurrencyUnit), id);
            return entity;
        }
    }

    /// <summary>Tenant, global (host) birimi düzenleyemez/silemez — yalnız host yönetir.</summary>
    private void EnsureEditable(CurrencyUnit entity, bool isDelete = false)
    {
        if (entity.TenantId == null && CurrentTenant.Id != null)
        {
            throw new BusinessException(isDelete
                ? "TradeXpress:CurrencyUnit:CannotDeleteGlobalAsTenant"
                : "TradeXpress:CurrencyUnit:CannotEditGlobalAsTenant");
        }
    }

    /// <summary>Takip ilişkisini kurar; tek-seviye kuralını repo ile doğrular (parent kendisi takip-eden olamaz).</summary>
    private async Task ApplyFollowingAsync(CurrencyUnit entity, Guid? followingUnitId, MarginType? marginType, decimal? marginValue)
    {
        if (followingUnitId is null)
        {
            entity.SetFollowing(null, null);
            return;
        }

        if (marginType is null || marginValue is null)
            throw new BusinessException("TradeXpress:CurrencyUnit:FollowingMarginRequired");

        var parent = await GetInScopeAsync(followingUnitId.Value);
        if (parent.IsFollowing)
            throw new BusinessException("TradeXpress:CurrencyUnit:FollowMustBeSingleLevel");

        entity.SetFollowing(followingUnitId, new MarginSetting(marginType.Value, marginValue.Value));
    }

    private CurrencyUnitListDto ToListDto(CurrencyUnit e, List<CurrencyUnit>? parents = null)
    {
        var dto = ObjectMapper.Map<CurrencyUnit, CurrencyUnitListDto>(e);
        dto.IsGlobal = e.TenantId == null;
        dto.FollowingMarginType = e.FollowingMargin?.Type;
        dto.FollowingMarginValue = e.FollowingMargin?.Value;
        if (parents != null && e.FollowingUnitId.HasValue)
        {
            var parent = parents.FirstOrDefault(x => x.Id == e.FollowingUnitId.Value);
            dto.FollowingUnitCode = parent?.Code;
        }
        return dto;
    }

    private CurrencyUnitGetDto ToGetDto(CurrencyUnit e)
    {
        var dto = ObjectMapper.Map<CurrencyUnit, CurrencyUnitGetDto>(e);
        dto.IsGlobal = e.TenantId == null;
        return dto;
    }
}
