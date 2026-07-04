using Volo.Abp.Identity;

namespace Integration.TradeXpress.Authorization;

/// <summary>
/// Geri-uyum grant seed'i (Faz 4 working-context yetkisi) — resolution-time doğrulama devreye girince
/// grant'sız kullanıcı şube seçemez → KİLİTLENİR. Bu seeder aktif tenant'ın TÜM kullanıcılarına
/// tenant-geneli (Company/Branch/Vault = null = "aşağıdaki her şey") Grant garanti ederek mevcut davranışı
/// korur.
///
/// <para><b>Grant biçimi:</b> kullanıcının HER rolü için tenant-geneli Grant (RoleId dolu) → hem resolver
/// (coğrafi erişim) hem ileriki permission-provider (her rol tenant-geneli geçerli). Kullanıcının HİÇ rolü
/// yoksa tek bir "coğrafi-only" Grant (RoleId=null, PermissionName=null) → permission boyutuna sahte izin
/// adı SOKMADAN yalnız coğrafi erişim taşır (kilitlenmeyi önler; onboarding'de ek kullanıcılar rolsüz
/// oluşturulduğundan bu yaygın bir durumdur).</para>
///
/// <para><b>İdempotent:</b> yalnız EKSİK tenant-geneli grant'ları ekler; zaten var olanı (aynı rol ya da
/// zaten bir coğrafi-only) atlar → ikinci koşu no-op. <see cref="MultiCompany.CompanyOwnedBackfiller"/>
/// idempotency desenini izler. Aktif tenant kapsamında çalışır (ABP DataSeeder CurrentTenant'ı
/// context.TenantId'ye ayarlar; yeni-kullanıcı handler'ı kullanıcının tenant'ına geçer); çağıran her tenant
/// için ayrı tetikler.</para>
/// </summary>
public class ScopedGrantSeeder : DomainService
{
    #region Fields

    private readonly IIdentityUserRepository _userRepository;
    private readonly IRepository<UserScopedGrant, Guid> _grantRepository;

    #endregion

    #region Constructors

    public ScopedGrantSeeder(
        IIdentityUserRepository userRepository,
        IRepository<UserScopedGrant, Guid> grantRepository)
    {
        _userRepository = userRepository;
        _grantRepository = grantRepository;
    }

    #endregion

    #region Seeding

    /// <summary>Aktif tenant'ın tüm kullanıcılarına eksik tenant-geneli grant'ları ekler (idempotent).</summary>
    public async Task SeedCurrentTenantAsync()
    {
        // sorting arg = Identity repo'nun kendi GetListAsync aşırı-yüklemesini seçer (IBasicRepository ile belirsizlik önlenir).
        var users = await _userRepository.GetListAsync(sorting: nameof(IdentityUser.Id));
        foreach (var user in users)
        {
            await EnsureTenantWideGrantsAsync(user.Id);
        }
    }

    /// <summary>
    /// Tek kullanıcıya tenant-geneli grant garantisi (yeni-kullanıcı handler'ı da bunu çağırır). Rolü varsa
    /// her rol için (eksikse) Grant; rolsüzse tek coğrafi-only Grant. Zaten var olan atlanır (idempotent).
    /// </summary>
    public async Task EnsureTenantWideGrantsAsync(Guid userId)
    {
        var existing = await GetTenantWideGrantsAsync(userId);
        var existingRoleIds = existing
            .Where(g => g.RoleId.HasValue)
            .Select(g => g.RoleId!.Value)
            .ToHashSet();
        var hasGeographicOnly = existing.Any(g => !g.RoleId.HasValue && string.IsNullOrEmpty(g.PermissionName));

        var roles = await _userRepository.GetRolesAsync(userId);
        if (roles.Count > 0)
        {
            foreach (var role in roles)
            {
                if (existingRoleIds.Contains(role.Id))
                {
                    continue; // bu rol için tenant-geneli grant zaten var → atla
                }

                await InsertTenantWideGrantAsync(userId, role.Id);
            }

            return;
        }

        // Rolsüz kullanıcı: tek coğrafi-only tenant-geneli grant (zaten varsa atla).
        if (hasGeographicOnly == false)
        {
            await InsertTenantWideGrantAsync(userId, roleId: null);
        }
    }

    #endregion

    #region Helpers

    /// <summary>Kullanıcının tenant-geneli (Company/Branch/Vault hepsi null) grant'larını getirir.</summary>
    private async Task<List<UserScopedGrant>> GetTenantWideGrantsAsync(Guid userId)
    {
        var query = (await _grantRepository.GetQueryableAsync())
            .Where(g =>
                g.UserId == userId &&
                g.TenantId == CurrentTenant.Id &&
                g.CompanyId == null &&
                g.BranchId == null &&
                g.VaultId == null);
        return await AsyncExecuter.ToListAsync(query);
    }

    /// <summary>Tenant-geneli Grant ekler (roleId null = coğrafi-only). Kapsam koordinatları hep null.</summary>
    private async Task InsertTenantWideGrantAsync(Guid userId, Guid? roleId)
    {
        var grant = new UserScopedGrant(
            userId: userId,
            roleId: roleId,
            permissionName: null,
            companyId: null,
            branchId: null,
            vaultId: null,
            mode: ScopedGrantMode.Grant);
        await _grantRepository.InsertAsync(grant, autoSave: true);
    }

    #endregion
}
