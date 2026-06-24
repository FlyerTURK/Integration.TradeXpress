using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Jewelries;

/// <summary>
/// Jewelry (Mücevher) CRUD — company-scoped. Görünür = host(TenantId null) + çalışılan şirkete-özel
/// (CompanyId == input.CompanyId; CompanyId null = holding-host). Sıralama: Code artan.
/// </summary>
[Authorize]
public class JewelryAppService : TradeXpressAppService, IJewelryAppService
{
    private readonly IRepository<Jewelry, Guid> _repository;
    private readonly IDataFilter _dataFilter;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public JewelryAppService(IRepository<Jewelry, Guid> repository, IDataFilter dataFilter)
    {
        _repository = repository;
        _dataFilter = dataFilter;
    }

    public virtual async Task<PagedResultDto<JewelryListDto>> GetListAsync(JewelryListRequestDto input)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var query = (await _repository.GetQueryableAsync())
                .Where(x => x.TenantId == null
                            || (x.TenantId == tenantId && (x.CompanyId == null || x.CompanyId == input.CompanyId)))
                .ApplyListRequest(input, AllowedListFields);

            var totalCount = await AsyncExecuter.CountAsync(query);
            var explicitSort = (input.Sorts is { Count: > 0 }) || !string.IsNullOrWhiteSpace(input.Sorting);
            if (!explicitSort)
                query = query.OrderBy(x => x.Code);

            var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));
            return new PagedResultDto<JewelryListDto>(totalCount, items.Select(ToListDto).ToList());
        }
    }

    public virtual async Task<JewelryGetDto> GetAsync(Guid id) => ToGetDto(await GetInScopeAsync(id));

    public virtual async Task<JewelryGetDto> CreateAsync(JewelryCreateDto input)
    {
        var entity = new Jewelry(
            input.Code, input.Name, input.CompanyId,
            input.IsQuantity, input.PriceByQuantity, input.PriceTypeChange,
            input.EntryPrice, input.EntryPriceUnitId, input.ExitPrice, input.ExitPriceUnitId);
        entity.SetAttributes(input.Model, input.Kind, input.Type, input.Color, input.Category, input.GroupCode);
        entity.SetDescription(input.Description);

        await _repository.InsertAsync(entity, autoSave: true);
        return ToGetDto(entity);
    }

    public virtual async Task<JewelryGetDto> UpdateAsync(Guid id, JewelryUpdateDto input)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity);

        entity.SetName(input.Name);
        entity.SetAttributes(input.Model, input.Kind, input.Type, input.Color, input.Category, input.GroupCode);
        entity.SetPricing(input.IsQuantity, input.PriceByQuantity, input.PriceTypeChange,
                          input.EntryPrice, input.EntryPriceUnitId, input.ExitPrice, input.ExitPriceUnitId);
        entity.SetDescription(input.Description);
        entity.SetActive(input.IsActive);

        await _repository.UpdateAsync(entity, autoSave: true);
        return ToGetDto(entity);
    }

    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity, isDelete: true);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    public virtual async Task<List<JewelryListDto>> GetPickerListAsync(Guid? companyId = null)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var rows = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.TenantId == null
                                || (x.TenantId == tenantId && (x.CompanyId == null || x.CompanyId == companyId)))
                    .OrderBy(x => x.Code));
            return rows.Select(ToListDto).ToList();
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Jewelry> GetInScopeAsync(Guid id)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var entity = await AsyncExecuter.FirstOrDefaultAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId)));
            return entity ?? throw new EntityNotFoundException(typeof(Jewelry), id);
        }
    }

    private void EnsureEditable(Jewelry entity, bool isDelete = false)
    {
        if (entity.TenantId == null && CurrentTenant.Id != null)
        {
            throw new BusinessException(isDelete
                ? "TradeXpress:Jewelry:CannotDeleteGlobalAsTenant"
                : "TradeXpress:Jewelry:CannotEditGlobalAsTenant");
        }
    }

    private static JewelryListDto ToListDto(Jewelry j) => new()
    {
        Id               = j.Id,
        Code             = j.Code,
        Name             = j.Name,
        Model            = j.Model,
        Kind             = j.Kind,
        IsQuantity       = j.IsQuantity,
        PriceByQuantity  = j.PriceByQuantity,
        PriceTypeChange  = j.PriceTypeChange,
        EntryPrice       = j.EntryPrice,
        EntryPriceUnitId = j.EntryPriceUnitId,
        ExitPrice        = j.ExitPrice,
        ExitPriceUnitId  = j.ExitPriceUnitId,
        CompanyId        = j.CompanyId,
        IsActive         = j.IsActive,
        IsGlobal         = j.TenantId == null,
    };

    private static JewelryGetDto ToGetDto(Jewelry j) => new()
    {
        Id               = j.Id,
        Code             = j.Code,
        Name             = j.Name,
        Model            = j.Model,
        Kind             = j.Kind,
        Type             = j.Type,
        Color            = j.Color,
        Category         = j.Category,
        GroupCode        = j.GroupCode,
        IsQuantity       = j.IsQuantity,
        PriceByQuantity  = j.PriceByQuantity,
        PriceTypeChange  = j.PriceTypeChange,
        EntryPrice       = j.EntryPrice,
        EntryPriceUnitId = j.EntryPriceUnitId,
        ExitPrice        = j.ExitPrice,
        ExitPriceUnitId  = j.ExitPriceUnitId,
        Description      = j.Description,
        CompanyId        = j.CompanyId,
        IsActive         = j.IsActive,
        IsGlobal         = j.TenantId == null,
    };
}
