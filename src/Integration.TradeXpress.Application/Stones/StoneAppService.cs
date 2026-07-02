using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Stones;

/// <summary>
/// Stone (Taş) CRUD. Görünürlük (Cash gibi): host kataloğu (TenantId=null) + tenant kendi kayıtları.
/// Tenant global kaydı düzenleyemez/silemez. Sıralama: Code artan.
/// </summary>
[Authorize]
public class StoneAppService : TradeXpressAppService, IStoneAppService
{
    private readonly IRepository<Stone, Guid> _repository;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentCompany _currentCompany;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public StoneAppService(IRepository<Stone, Guid> repository, IDataFilter dataFilter, ICurrentCompany currentCompany)
    {
        _repository = repository;
        _dataFilter = dataFilter;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<StoneListDto>> GetListAsync(StoneListRequestDto input)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            // Görünürlük merkezi helper'dan (host + holding-host + çalışılan şirket); şirket ambient'ten.
            var query = (await _repository.GetQueryableAsync())
                .WhereCompanyVisible(CurrentTenant.Id, _currentCompany.Id)
                .ApplyListRequest(input, AllowedListFields);

            var totalCount = await AsyncExecuter.CountAsync(query);
            var explicitSort = (input.Sorts is { Count: > 0 }) || !string.IsNullOrWhiteSpace(input.Sorting);
            if (!explicitSort)
                query = query.OrderBy(x => x.Code);

            var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));
            return new PagedResultDto<StoneListDto>(totalCount, items.Select(MapList).ToList());
        }
    }

    public virtual async Task<StoneGetDto> GetAsync(Guid id) => MapGet(await GetInScopeAsync(id));

    public virtual async Task<StoneGetDto> CreateAsync(StoneCreateDto input)
    {
        var entity = new Stone(
            input.Code, input.Name, input.CompanyId,
            input.IsQuantity, input.PriceByQuantity, input.PriceTypeChange,
            input.EntryPrice, input.EntryPriceUnitId, input.ExitPrice, input.ExitPriceUnitId);
        entity.SetAttributes(input.StoneKind, input.StoneType, input.Color, input.Cut,
                             input.Clarity, input.Sieve, input.Category, input.GroupCode);
        entity.SetDescription(input.Description);

        await _repository.InsertAsync(entity, autoSave: true);
        return MapGet(entity);
    }

    public virtual async Task<StoneGetDto> UpdateAsync(Guid id, StoneUpdateDto input)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity);

        entity.SetName(input.Name);
        entity.SetAttributes(input.StoneKind, input.StoneType, input.Color, input.Cut,
                             input.Clarity, input.Sieve, input.Category, input.GroupCode);
        entity.SetPricing(input.IsQuantity, input.PriceByQuantity, input.PriceTypeChange,
                          input.EntryPrice, input.EntryPriceUnitId, input.ExitPrice, input.ExitPriceUnitId);
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

    public virtual async Task<List<StoneListDto>> GetPickerListAsync(Guid? companyId = null)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var rows = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .WhereCompanyVisible(CurrentTenant.Id, companyId ?? _currentCompany.Id)
                    .OrderBy(x => x.Code));
            return rows.Select(MapList).ToList();
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Stone> GetInScopeAsync(Guid id)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var entity = await AsyncExecuter.FirstOrDefaultAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId)));
            return entity ?? throw new EntityNotFoundException(typeof(Stone), id);
        }
    }

    private void EnsureEditable(Stone entity, bool isDelete = false)
    {
        if (entity.TenantId == null && CurrentTenant.Id != null)
        {
            throw new BusinessException(isDelete
                ? "TradeXpress:Stone:CannotDeleteGlobalAsTenant"
                : "TradeXpress:Stone:CannotEditGlobalAsTenant");
        }
    }

    // Mapperly + IsGlobal enrichment. Instance → net'i tetiklemez.
    private StoneListDto MapList(Stone s)
    {
        var dto = ObjectMapper.Map<Stone, StoneListDto>(s);
        dto.IsGlobal = s.TenantId == null;
        return dto;
    }

    private StoneGetDto MapGet(Stone s)
    {
        var dto = ObjectMapper.Map<Stone, StoneGetDto>(s);
        dto.IsGlobal = s.TenantId == null;
        return dto;
    }
}
