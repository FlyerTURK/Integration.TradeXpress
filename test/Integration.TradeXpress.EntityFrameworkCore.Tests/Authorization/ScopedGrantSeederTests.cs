using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Authorization;

/// <summary>
/// <see cref="ScopedGrantSeeder"/> + <see cref="ScopedGrantUserCreatedHandler"/> geri-uyum ağı (Faz 4
/// working-context): resolution-time doğrulama devreye girince grant'sız kullanıcı şube seçemez →
/// kilitlenir. Bu ağ, mevcut/yeni kullanıcılara tenant-geneli Grant garanti edildiğini doğrular
/// (rollüye rol-başı Grant, rolsüze coğrafi-only Grant), idempotent. <see cref="ScopedGrantResolverTests"/>
/// deseni: tek tenant, kimlikler test-başına benzersiz (paylaşılan Sqlite collection DB'si).
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class ScopedGrantSeederTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ScopedGrantSeeder _seeder;
    private readonly IRepository<UserScopedGrant, Guid> _grants;
    private readonly IdentityUserManager _userManager;
    private readonly IdentityRoleManager _roleManager;
    private readonly ICurrentTenant _currentTenant;

    public ScopedGrantSeederTests()
    {
        _seeder        = GetRequiredService<ScopedGrantSeeder>();
        _grants        = GetRequiredService<IRepository<UserScopedGrant, Guid>>();
        _userManager   = GetRequiredService<IdentityUserManager>();
        _roleManager   = GetRequiredService<IdentityRoleManager>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    // (Handler) Yeni ROLSÜZ kullanıcı oluşturulunca local event handler tek coğrafi-only Grant yazar.
    [Fact]
    public async Task Handler_grants_geography_only_to_new_roleless_user()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();

        var userId = await CreateUserAsync(tenantId);

        var grants = await GetGrantsAsync(tenantId, userId);
        grants.Count.ShouldBe(1);
        grants[0].RoleId.ShouldBeNull();
        grants[0].PermissionName.ShouldBeNull();
        grants[0].CompanyId.ShouldBeNull();
        grants[0].BranchId.ShouldBeNull();
        grants[0].VaultId.ShouldBeNull();
        grants[0].Mode.ShouldBe(ScopedGrantMode.Grant);
    }

    // (a) Rolü olan kullanıcıya batch seed tenant-geneli (rol-başı) Grant oluşturur.
    [Fact]
    public async Task Seeder_creates_tenant_wide_grant_for_user_with_role()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var (roleId, roleName) = await CreateRoleAsync(tenantId);
        var userId = await CreateUserAsync(tenantId, roleName);

        // Handler'ın oluşturduğu coğrafi-only'yi temizle → batch seed'in rol-grant yolunu izole doğrula.
        await ClearGrantsAsync(tenantId, userId);

        await SeedAsync(tenantId);

        var grants = await GetGrantsAsync(tenantId, userId);
        grants.Count.ShouldBe(1);
        grants[0].RoleId.ShouldBe(roleId);
        grants[0].CompanyId.ShouldBeNull();
        grants[0].BranchId.ShouldBeNull();
        grants[0].VaultId.ShouldBeNull();
        grants[0].Mode.ShouldBe(ScopedGrantMode.Grant);
    }

    // (b) İkinci koşu idempotent no-op — aynı rol için ikinci Grant üretilmez.
    [Fact]
    public async Task Seeder_second_run_is_noop()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var (_, roleName) = await CreateRoleAsync(tenantId);
        var userId = await CreateUserAsync(tenantId, roleName);
        await ClearGrantsAsync(tenantId, userId);

        await SeedAsync(tenantId);
        await SeedAsync(tenantId);

        var grants = await GetGrantsAsync(tenantId, userId);
        grants.Count.ShouldBe(1);
    }

    // (c) Zaten tenant-geneli grant'ı olan kullanıcı atlanır (ikinci koşu sayıyı artırmaz).
    [Fact]
    public async Task Seeder_skips_user_that_already_has_grant()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var (_, roleName) = await CreateRoleAsync(tenantId);
        var userId = await CreateUserAsync(tenantId, roleName);

        // Handler coğrafi-only + ilk seed rol-grant → kullanıcının zaten tenant-geneli grant'ı var.
        await SeedAsync(tenantId);
        var before = await GetGrantsAsync(tenantId, userId);

        await SeedAsync(tenantId);
        var after = await GetGrantsAsync(tenantId, userId);

        after.Count.ShouldBe(before.Count);
    }

    // ── kurulum / yardımcılar ────────────────────────────────────────────────

    private Task<Guid> CreateUserAsync(Guid tenantId, string? roleName = null)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var suffix = SimpleGuidGenerator.Instance.Create().ToString("N");
                var user = new IdentityUser(
                    SimpleGuidGenerator.Instance.Create(), $"u{suffix}", $"{suffix}@t.io", tenantId);
                (await _userManager.CreateAsync(user)).CheckErrors();

                if (roleName != null)
                {
                    (await _userManager.AddToRoleAsync(user, roleName)).CheckErrors();
                }

                return user.Id;
            }
        });
    }

    private Task<(Guid RoleId, string RoleName)> CreateRoleAsync(Guid tenantId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var name = $"role{SimpleGuidGenerator.Instance.Create():N}";
                var role = new IdentityRole(SimpleGuidGenerator.Instance.Create(), name, tenantId);
                (await _roleManager.CreateAsync(role)).CheckErrors();
                return (role.Id, role.Name);
            }
        });
    }

    private Task SeedAsync(Guid tenantId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                await _seeder.SeedCurrentTenantAsync();
            }
        });
    }

    private Task<List<UserScopedGrant>> GetGrantsAsync(Guid tenantId, Guid userId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                return await _grants.GetListAsync(g => g.UserId == userId);
            }
        });
    }

    private Task ClearGrantsAsync(Guid tenantId, Guid userId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(tenantId))
            {
                var list = await _grants.GetListAsync(g => g.UserId == userId);
                foreach (var grant in list)
                {
                    await _grants.DeleteAsync(grant, autoSave: true);
                }
            }
        });
    }
}
