using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.TenantManagement;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Organization;

namespace Integration.TradeXpress.Tenants;

[Authorize(TenantManagementPermissions.Tenants.Default)]
public class TenantAppService : TradeXpressAppService, ITenantAppService
{
    private readonly ITenantManager _tenantManager;
    private readonly ITenantRepository _tenantRepository;
    private readonly IReadOnlyRepository<Tenant, Guid> _tenantQueryRepository;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<Country, Guid> _countryRepository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly OrgTreeManager _orgTree;
    private readonly IDataFilter _dataFilter;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Name", "Id" };

    public TenantAppService(
        ITenantManager tenantManager,
        ITenantRepository tenantRepository,
        IReadOnlyRepository<Tenant, Guid> tenantQueryRepository,
        IDistributedEventBus distributedEventBus,
        IRepository<Company, Guid> companyRepository,
        IRepository<Country, Guid> countryRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        OrgTreeManager orgTree,
        IDataFilter dataFilter)
    {
        _tenantManager = tenantManager;
        _tenantRepository = tenantRepository;
        _tenantQueryRepository = tenantQueryRepository;
        _distributedEventBus = distributedEventBus;
        _companyRepository = companyRepository;
        _countryRepository = countryRepository;
        _unitRepository = unitRepository;
        _orgTree = orgTree;
        _dataFilter = dataFilter;
    }

    public virtual async Task<TenantGetDto> GetAsync(Guid id)
    {
        var tenant = await _tenantRepository.GetAsync(id);
        return ObjectMapper.Map<Tenant, TenantGetDto>(tenant);
    }

    public virtual async Task<PagedResultDto<TenantListDto>> GetListAsync(TenantListRequestDto input)
    {
        var query = (await _tenantQueryRepository.GetQueryableAsync())
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<TenantListDto>(
            totalCount,
            items.Select(t => ObjectMapper.Map<Tenant, TenantListDto>(t)).ToList());
    }

    [Authorize(TenantManagementPermissions.Tenants.Create)]
    public virtual async Task<TenantGetDto> CreateAsync(TenantCreateDto input)
    {
        var tenant = await _tenantManager.CreateAsync(input.Name);
        await _tenantRepository.InsertAsync(tenant, autoSave: true);

        // Tenant Admin kullanıcısı için DataSeeder'ı tetikle.
        await _distributedEventBus.PublishAsync(
            TenantCreatedEtoFactory.Create(tenant, input.AdminEmailAddress, input.AdminPassword));

        // Merkez (HQ) şirketini onboarding bilgileriyle kur (yeni tenant'ın scope'unda).
        await SetHeadquartersAsync(tenant.Id, input.HqCompanyName, input.HqCountryCode);

        return ObjectMapper.Map<Tenant, TenantGetDto>(tenant);
    }

    [Authorize(TenantManagementPermissions.Tenants.Update)]
    public virtual async Task<TenantGetDto> UpdateAsync(Guid id, TenantUpdateDto input)
    {
        var tenant = await _tenantRepository.GetAsync(id);
        await _tenantManager.ChangeNameAsync(tenant, input.Name);
        await _tenantRepository.UpdateAsync(tenant, autoSave: true);
        return ObjectMapper.Map<Tenant, TenantGetDto>(tenant);
    }

    [Authorize(TenantManagementPermissions.Tenants.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        await _tenantRepository.DeleteAsync(id);
    }

    /// <summary>
    /// Yeni tenant için merkez (HQ) şirketini kurar/günceller (idempotent). Ad boşsa atlanır
    /// (seed varsayılan HQ'yu kurar). Base para birimi ülkenin DefaultCurrencyCode'undan,
    /// yoksa pivot TRY'den çözülür. Ülke kodu boşsa TR varsayılır.
    /// </summary>
    private async Task SetHeadquartersAsync(Guid tenantId, string? companyName, string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            return;

        var country = (countryCode ?? "TR").Trim().ToUpperInvariant();

        using (CurrentTenant.Change(tenantId))
        using (_dataFilter.Disable<IMultiTenant>())
        {
            // Ülkenin varsayılan para birimi (global), yoksa TRY.
            var ccyCode = (await AsyncExecuter.FirstOrDefaultAsync(
                    (await _countryRepository.GetQueryableAsync())
                        .Where(c => c.TenantId == null && c.Code == country)))?.DefaultCurrencyCode
                ?? CurrencyUnitCode.TRY;

            var unitQuery = await _unitRepository.GetQueryableAsync();
            var baseUnit = await AsyncExecuter.FirstOrDefaultAsync(
                    unitQuery.Where(u => u.TenantId == null && u.Code == ccyCode))
                ?? await AsyncExecuter.FirstOrDefaultAsync(
                    unitQuery.Where(u => u.TenantId == null && u.Code == CurrencyUnitCode.TRY));
            if (baseUnit == null)
                return; // birimler henüz seed edilmemiş (host run gerekir)

            var existing = await AsyncExecuter.FirstOrDefaultAsync(
                (await _companyRepository.GetQueryableAsync())
                    .Where(c => c.TenantId == tenantId && c.IsHeadquarters));

            Company company;
            if (existing != null)
            {
                existing.SetName(companyName);
                existing.SetCountryCode(country);
                existing.SetBaseCurrency(baseUnit.Id);
                await _companyRepository.UpdateAsync(existing, autoSave: true);
                company = existing;
            }
            else
            {
                company = new Company("MRK", companyName, country, baseUnit.Id,
                    isHeadquarters: true, displayOrder: 1, tenantId: tenantId);
                await _companyRepository.InsertAsync(company, autoSave: true);
            }

            // Merkez şirket en az bir HQ şube + varsayılan kasayla doğar.
            await _orgTree.EnsureHeadquartersBranchAsync(company);
        }
    }
}
