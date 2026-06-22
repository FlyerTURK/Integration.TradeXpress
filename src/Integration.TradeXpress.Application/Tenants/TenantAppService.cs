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
using Volo.Abp.Identity;

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
    private readonly IdentityUserManager _userManager;

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
        IDataFilter dataFilter,
        IdentityUserManager userManager)
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
        _userManager = userManager;
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
        // Admin = IsAdmin işaretli ilk satır (yoksa ilk kullanıcı). Onun e-posta/şifresi ABP'nin
        // zorunlu tenant-admin'ini (admin rolü + kullanıcı) seed eder.
        var admin = input.Users.FirstOrDefault(u => u.IsAdmin) ?? input.Users.FirstOrDefault();

        var tenant = await _tenantManager.CreateAsync(input.Name);
        await _tenantRepository.InsertAsync(tenant, autoSave: true);

        if (admin != null)
        {
            await _distributedEventBus.PublishAsync(
                TenantCreatedEtoFactory.Create(tenant, admin.Email, admin.Password));
        }

        // Ek kullanıcılar + şirketler yeni tenant'ın scope'unda oluşturulur (global birim görünür kalsın).
        using (CurrentTenant.Change(tenant.Id))
        using (_dataFilter.Disable<IMultiTenant>())
        {
            foreach (var u in input.Users.Where(x => x != admin))
            {
                await CreateUserAsync(u);
            }

            foreach (var company in input.Companies)
            {
                await CreateCompanyAsync(tenant.Id, company);
            }
        }

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

    /// <summary>Onboarding kullanıcısını yeni tenant'ta oluşturur (çağrı CurrentTenant.Change scope'unda).</summary>
    private async Task CreateUserAsync(TenantUserInput input)
    {
        var user = new IdentityUser(GuidGenerator.Create(), input.UserName, input.Email, CurrentTenant.Id);
        var result = await _userManager.CreateAsync(user, input.Password);
        if (!result.Succeeded)
            throw new Volo.Abp.UserFriendlyException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    /// <summary>
    /// Onboarding şirketini yeni tenant'ta oluşturur (çağrı CurrentTenant.Change scope'unda).
    /// Base para birimi ülkenin DefaultCurrencyCode'undan, yoksa pivot TRY'den çözülür. Her şirket
    /// en az bir HQ şube + varsayılan kasayla doğar.
    /// </summary>
    private async Task CreateCompanyAsync(Guid tenantId, TenantCompanyInput input)
    {
        var country = (input.CountryCode ?? "TR").Trim().ToUpperInvariant();

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

        var company = new Company(input.Code, input.Name, country, baseUnit.Id,
            isHeadquarters: input.IsHeadquarters, displayOrder: 1, tenantId: tenantId);
        await _companyRepository.InsertAsync(company, autoSave: true);

        await _orgTree.EnsureHeadquartersBranchAsync(company);
    }
}
