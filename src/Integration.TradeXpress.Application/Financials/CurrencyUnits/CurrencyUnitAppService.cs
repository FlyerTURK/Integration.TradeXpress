using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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

namespace Integration.TradeXpress.Financials.CurrencyUnits;

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

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase)
        { "Code", "Name", "Type", "IsActive", "DisplayOrder", "Id", "AlwaysShowInBalance" };

    // Sort/filter alias'ı: IsGlobal entity'de yok (TenantId==null demek) → host-önce sıralaması için.
    private static readonly IReadOnlyDictionary<string, LambdaExpression> ListAliases =
        new Dictionary<string, LambdaExpression>(StringComparer.OrdinalIgnoreCase)
        {
            ["IsGlobal"] = (Expression<Func<CurrencyUnit, bool>>)(x => x.TenantId == null),
        };

    // Kullanıcı kolon sıralamadığında CurrencyUnit standart sıralaması:
    // Host (global) önce → Bakiyede gösterilenler önce → Sıra artan → Code artan (sonra Id tie-breaker).
    private static List<SortField> DefaultListSorts() => new()
    {
        new() { Field = "IsGlobal",            Descending = true  },
        new() { Field = "AlwaysShowInBalance", Descending = true  },
        new() { Field = "DisplayOrder",        Descending = false },
        new() { Field = "Code",                Descending = false },
    };

    public CurrencyUnitAppService(IRepository<CurrencyUnit, Guid> repository)
    {
        _repository = repository;
    }

    public virtual async Task<PagedResultDto<CurrencyUnitListDto>> GetListAsync(CurrencyUnitListRequestDto input)
    {
        // Multi-tenant filter'ı kapat, görünürlüğü kendimiz belirle (global + kendi).
        using (DataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;

            // Kullanıcı bir sıralama vermediyse CurrencyUnit'e özel standart sıralamayı uygula.
            if ((input.Sorts == null || input.Sorts.Count == 0) && string.IsNullOrWhiteSpace(input.Sorting))
                input.Sorts = DefaultListSorts();

            var query = (await _repository.GetQueryableAsync())
                .Where(x => x.TenantId == null || x.TenantId == tenantId)
                .ApplyListRequest(input, AllowedListFields, ListAliases);

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
            input.Code,
            input.Name,
            input.Type,
            displayOrder: input.DisplayOrder);

        entity.SetDescription(input.Description);
        entity.SetAlwaysShowInBalance(input.AlwaysShowInBalance);
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
        entity.SetAlwaysShowInBalance(input.AlwaysShowInBalance);
        entity.SetActive(input.IsActive);

        await ApplyFollowingAsync(entity, input.FollowingUnitId, input.FollowingMarginType, input.FollowingMarginValue);

        await _repository.UpdateAsync(entity, autoSave: true);
        return ToGetDto(entity);
    }

    [Authorize(TradeXpressPermissions.CurrencyUnits.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity, isDelete: true);

        await _repository.DeleteAsync(entity, autoSave: true);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Id'yi görünürlük scope'unda (global + kendi) çeker; yoksa EntityNotFound.</summary>
    private async Task<CurrencyUnit> GetInScopeAsync(Guid id)
    {
        using (DataFilter.Disable<IMultiTenant>())
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
        dto.IsSystem = e.TenantId == null;
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
        dto.IsSystem = e.TenantId == null;
        return dto;
    }
}
