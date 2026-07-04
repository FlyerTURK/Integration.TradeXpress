using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.TenantManagement;
using Microsoft.AspNetCore.Authorization;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Companies;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;

namespace Integration.TradeXpress.Tenants;

/// <summary>
/// Tenant CRUD + onboarding. Yeni tenant akışı (hepsi tek UoW → atomik):
/// <list type="number">
/// <item>Tenant oluştur.</item>
/// <item><b>Admin'i SENKRON seed et</b> (admin rolü + TÜM izinler + admin kullanıcı) — ABP'nin
/// post-commit event'i yerine inline, çünkü izinler bir sonraki adımda lazım.</item>
/// <item><b>Admin'i impersonate et</b> (<c>ICurrentPrincipalAccessor.Change</c> = "ChangeUser") →
/// principal artık tenant'ın tüm izinlerine sahip.</item>
/// <item>Ek kullanıcılar + şirket graflarını <see cref="ICompanyAppService"/>'e DELEGE et (o da
/// şubeleri/kasaları kendi app-service'lerine) → Tenant→Company→Branch→Vault recursive. Auth GERÇEKTEN
/// geçer (bypass değil): işlemler tenant admin'i olarak çalışır.</item>
/// </list>
/// </summary>
[Authorize(TenantManagementPermissions.Tenants.Default)]
public class TenantAppService : TradeXpressAppService, ITenantAppService
{
    private readonly ITenantManager _tenantManager;
    private readonly ITenantRepository _tenantRepository;
    private readonly IReadOnlyRepository<Tenant, Guid> _tenantQueryRepository;
    private readonly ICompanyAppService _companyAppService;   // şirket grafı yazımı buraya delege
    private readonly IdentityUserManager _userManager;
    private readonly IDataSeeder _dataSeeder;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Name", "Id" };

    public TenantAppService(
        ITenantManager tenantManager,
        ITenantRepository tenantRepository,
        IReadOnlyRepository<Tenant, Guid> tenantQueryRepository,
        ICompanyAppService companyAppService,
        IdentityUserManager userManager,
        IDataSeeder dataSeeder,
        ICurrentPrincipalAccessor currentPrincipalAccessor)
    {
        _tenantManager = tenantManager;
        _tenantRepository = tenantRepository;
        _tenantQueryRepository = tenantQueryRepository;
        _companyAppService = companyAppService;
        _userManager = userManager;
        _dataSeeder = dataSeeder;
        _currentPrincipalAccessor = currentPrincipalAccessor;
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
        // Admin = IsAdmin işaretli ilk satır (yoksa ilk kullanıcı). E-posta/şifresi tenant-admin'ini seed eder.
        var admin = input.Users.FirstOrDefault(u => u.IsAdmin) ?? input.Users.FirstOrDefault();

        var tenant = await _tenantManager.CreateAsync(input.Name);
        await _tenantRepository.InsertAsync(tenant, autoSave: true);

        using (CurrentTenant.Change(tenant.Id))
        {
            // 1) Admin'i SENKRON seed et (admin rolü + TÜM izinler + kullanıcı). İzinler 3. adımda lazım.
            if (admin != null)
            {
                await _dataSeeder.SeedAsync(new DataSeedContext(tenant.Id)
                    .WithProperty(IdentityDataSeedContributor.AdminEmailPropertyName, admin.Email)
                    .WithProperty(IdentityDataSeedContributor.AdminPasswordPropertyName, admin.Password)
                    // Org'u onboarding kendisi kuruyor → OrgSeeder'ın varsayılan MRK'sını atla (çift kayıt önle).
                    .WithProperty(TradeXpressDataSeedContributor.SkipOrgSeedProperty, true));
            }

            // 2) Seed edilen admin'i bul + impersonate (ChangeUser) → tenant izinlerine sahip principal.
            var adminUser = admin != null ? await _userManager.FindByEmailAsync(admin.Email) : null;
            var adminPrincipal = adminUser != null ? await BuildPrincipalAsync(adminUser) : null;

            using (adminPrincipal != null ? _currentPrincipalAccessor.Change(adminPrincipal) : (IDisposable?)null)
            {
                // 3) Ek kullanıcılar
                foreach (var u in input.Users.Where(x => x != admin))
                {
                    await CreateUserAsync(u);
                }

                // 4) Şirket grafları — app-service delegasyonu (artık tenant admin olarak yetkili).
                //    HQ şirket önce (sonra gelen merkez değilse mevcut HQ'yu bozmaz).
                foreach (var company in input.Companies.OrderByDescending(c => c.IsHeadquarters))
                {
                    await CreateCompanyAsync(company);
                }
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

    /// <summary>Seed edilen admin için impersonation principal'i kurar (userId + tenantId + rol claim'leri).</summary>
    private async Task<ClaimsPrincipal> BuildPrincipalAsync(IdentityUser adminUser)
    {
        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, adminUser.Id.ToString()),
            new(AbpClaimTypes.UserName, adminUser.UserName),
        };
        if (adminUser.TenantId.HasValue)
            claims.Add(new Claim(AbpClaimTypes.TenantId, adminUser.TenantId.Value.ToString()));
        if (!string.IsNullOrEmpty(adminUser.Email))
            claims.Add(new Claim(AbpClaimTypes.Email, adminUser.Email));

        foreach (var role in await _userManager.GetRolesAsync(adminUser))
            claims.Add(new Claim(AbpClaimTypes.Role, role));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Impersonation"));
    }

    /// <summary>Onboarding kullanıcısını yeni tenant'ta oluşturur (çağrı CurrentTenant.Change scope'unda).</summary>
    private async Task CreateUserAsync(TenantUserInput input)
    {
        var user = new IdentityUser(GuidGenerator.Create(), input.UserName, input.Email, CurrentTenant.Id);
        var result = await _userManager.CreateAsync(user, input.Password);
        if (!result.Succeeded)
            throw new UserFriendlyException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    /// <summary>Onboarding şirket grafını (şube→kasa) CompanyAppService'e delege eder (tenant admin yetkisiyle).</summary>
    private async Task CreateCompanyAsync(CompanyGraphDto input)
    {
        await _companyAppService.CreateAsync(new CompanyCreateDto
        {
            Code = input.Code,
            Name = input.Name,
            CountryId = input.CountryId,
            BaseCurrencyUnitId = input.BaseCurrencyUnitId,
            IsHeadquarters = input.IsHeadquarters,
            DisplayOrder = input.DisplayOrder,
            Description = input.Description,
            Branches = input.Branches,
        });
    }
}
