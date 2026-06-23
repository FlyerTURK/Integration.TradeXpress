using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Countries;

/// <summary>
/// Ülke kataloğu CRUD. Merkezi referans: <b>host global listeyi yönetir, tenant'lar görür</b>
/// (görünürlük null‖own; tenant global ülkeyi düzenleyemez, kendi ekleyebilir). Tenant HQ
/// şirketi bu katalogdan ülke seçer.
/// </summary>
[Authorize(TradeXpressPermissions.Countries.Default)]
public class CountryAppService : TradeXpressAppService, ICountryAppService
{
    private readonly IRepository<Country, Guid> _repository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;  // yalnız OKUMA (DefaultCurrencyCode→Id, link)
    private readonly IDataFilter _dataFilter;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "DisplayOrder", "Id" };

    public CountryAppService(
        IRepository<Country, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IDataFilter dataFilter)
    {
        _repository = repository;
        _unitRepository = unitRepository;
        _dataFilter = dataFilter;
    }

    public virtual async Task<PagedResultDto<CountryListDto>> GetListAsync(CountryListRequestDto input)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var query = (await _repository.GetQueryableAsync())
                .Where(x => x.TenantId == null || x.TenantId == tenantId)
                .ApplyListRequest(input, AllowedListFields);

            var totalCount = await AsyncExecuter.CountAsync(query);
            var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

            // DefaultCurrencyCode string kolon (FK yok) → linklenebilmesi için CurrencyUnit.Id'yi Code'dan
            // çöz (bellekte; bu sayfanın kodları). Kapsam: global (TenantId=null) + mevcut tenant.
            var ccyCodes = items.Select(c => c.DefaultCurrencyCode).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            var unitMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            if (ccyCodes.Count > 0)
            {
                var units = await _unitRepository.GetQueryableAsync();
                var matched = await AsyncExecuter.ToListAsync(
                    units.Where(u => ccyCodes.Contains(u.Code) && (u.TenantId == null || u.TenantId == tenantId)));
                foreach (var u in matched)
                    unitMap[u.Code] = u.Id;
            }

            return new PagedResultDto<CountryListDto>(totalCount, items.Select(c => ToListDto(c, unitMap)).ToList());
        }
    }

    public virtual async Task<CountryGetDto> GetAsync(Guid id) => ToGetDto(await GetInScopeAsync(id));

    [Authorize(TradeXpressPermissions.Countries.Create)]
    public virtual async Task<CountryGetDto> CreateAsync(CountryCreateDto input)
    {
        var entity = new Country(input.Code, input.Name, input.DefaultCurrencyCode, input.DisplayOrder);
        await _repository.InsertAsync(entity, autoSave: true);
        return ToGetDto(entity);
    }

    [Authorize(TradeXpressPermissions.Countries.Update)]
    public virtual async Task<CountryGetDto> UpdateAsync(Guid id, CountryUpdateDto input)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity);
        entity.SetName(input.Name);
        entity.SetDefaultCurrencyCode(input.DefaultCurrencyCode);
        entity.SetDisplayOrder(input.DisplayOrder);
        if (input.IsActive) entity.Activate(); else entity.Deactivate();
        await _repository.UpdateAsync(entity, autoSave: true);
        return ToGetDto(entity);
    }

    [Authorize(TradeXpressPermissions.Countries.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Country> GetInScopeAsync(Guid id)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var entity = await AsyncExecuter.FirstOrDefaultAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId)));
            return entity ?? throw new EntityNotFoundException(typeof(Country), id);
        }
    }

    private void EnsureEditable(Country entity)
    {
        if (entity.TenantId == null && CurrentTenant.Id != null)
            throw new BusinessException("TradeXpress:Country:CannotEditGlobalAsTenant");
    }

    private static CountryListDto ToListDto(Country c, IReadOnlyDictionary<string, Guid> unitMap) => new()
    {
        Id = c.Id, Code = c.Code, Name = c.Name, DefaultCurrencyCode = c.DefaultCurrencyCode,
        DefaultCurrencyUnitId = !string.IsNullOrEmpty(c.DefaultCurrencyCode) && unitMap.TryGetValue(c.DefaultCurrencyCode, out var uid)
            ? uid : (Guid?)null,
        IsActive = c.IsActive, DisplayOrder = c.DisplayOrder, IsGlobal = c.TenantId == null,
    };

    private CountryGetDto ToGetDto(Country c) => new()
    {
        Id = c.Id, Code = c.Code, Name = c.Name, DefaultCurrencyCode = c.DefaultCurrencyCode,
        IsActive = c.IsActive, DisplayOrder = c.DisplayOrder, IsGlobal = c.TenantId == null,
    };
}
