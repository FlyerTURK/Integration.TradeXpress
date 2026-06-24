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

namespace Integration.TradeXpress.Services;

/// <summary>
/// Service (Hizmet) CRUD. Görünürlük (Cash gibi): host kataloğu (TenantId=null) herkese görünür +
/// tenant kendi kayıtlarını görür → multi-tenant filter disable + <c>TenantId == null || == own</c>.
/// Tenant, global (host) kaydı düzenleyemez/silemez.
/// </summary>
[Authorize]
public class ServiceAppService : TradeXpressAppService, IServiceAppService
{
    private readonly IRepository<Service, Guid> _repository;
    private readonly IDataFilter _dataFilter;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public ServiceAppService(IRepository<Service, Guid> repository, IDataFilter dataFilter)
    {
        _repository = repository;
        _dataFilter = dataFilter;
    }

    public virtual async Task<PagedResultDto<ServiceListDto>> GetListAsync(ServiceListRequestDto input)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var query = (await _repository.GetQueryableAsync())
                .Where(x => x.TenantId == null || x.TenantId == tenantId)
                .ApplyListRequest(input, AllowedListFields);

            var totalCount = await AsyncExecuter.CountAsync(query);
            var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

            return new PagedResultDto<ServiceListDto>(totalCount, items.Select(ToListDto).ToList());
        }
    }

    public virtual async Task<ServiceGetDto> GetAsync(Guid id) => ToGetDto(await GetInScopeAsync(id));

    public virtual async Task<ServiceGetDto> CreateAsync(ServiceCreateDto input)
    {
        var entity = new Service(input.Code, input.Name);   // TenantId otomatik (host→null, tenant→kendi)
        entity.SetDescription(input.Description);

        await _repository.InsertAsync(entity, autoSave: true);
        return ToGetDto(entity);
    }

    public virtual async Task<ServiceGetDto> UpdateAsync(Guid id, ServiceUpdateDto input)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity);

        entity.SetName(input.Name);
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

    public virtual async Task<List<ServiceListDto>> GetPickerListAsync()
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var rows = await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.TenantId == null || x.TenantId == tenantId)
                    .OrderBy(x => x.Code));

            return rows.Select(ToListDto).ToList();
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Service> GetInScopeAsync(Guid id)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var entity = await AsyncExecuter.FirstOrDefaultAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId)));

            return entity ?? throw new EntityNotFoundException(typeof(Service), id);
        }
    }

    private void EnsureEditable(Service entity, bool isDelete = false)
    {
        if (entity.TenantId == null && CurrentTenant.Id != null)
        {
            throw new BusinessException(isDelete
                ? "TradeXpress:Service:CannotDeleteGlobalAsTenant"
                : "TradeXpress:Service:CannotEditGlobalAsTenant");
        }
    }

    private static ServiceListDto ToListDto(Service s) => new()
    {
        Id       = s.Id,
        Code     = s.Code,
        Name     = s.Name,
        IsActive = s.IsActive,
        IsGlobal = s.TenantId == null,
    };

    private static ServiceGetDto ToGetDto(Service s) => new()
    {
        Id          = s.Id,
        Code        = s.Code,
        Name        = s.Name,
        Description = s.Description,
        IsActive    = s.IsActive,
        IsGlobal    = s.TenantId == null,
    };
}
