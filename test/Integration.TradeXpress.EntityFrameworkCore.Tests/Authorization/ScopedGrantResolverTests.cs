using System;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Authorization;

/// <summary>
/// <see cref="IScopedGrantResolver"/> çözümleme ağı: gerçek DB + cache üzerinden en-spesifik-kazanır ve
/// eşit-seviyede-Deny-üstün semantiğini doğrular. Tek tenant, iki şirket; kodlar/kimlikler test-başına
/// benzersiz (paylaşılan Sqlite collection DB'si) — <see cref="MultiCompany.CompanyScopedFilterTests"/> deseni.
///
/// KRİTİK ayrım (senaryo c): "şirket Deny + daha spesifik şube Grant" → şube ERİŞİLİR (Grant daha
/// spesifik). Deny yalnız AYNI özgüllük seviyesinde kesin üstündür; daha spesifik bir Grant'i EZMEZ.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class ScopedGrantResolverTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IScopedGrantResolver _resolver;
    private readonly IRepository<UserScopedGrant, Guid> _grants;
    private readonly ICurrentTenant _currentTenant;

    public ScopedGrantResolverTests()
    {
        _resolver      = GetRequiredService<IScopedGrantResolver>();
        _grants        = GetRequiredService<IRepository<UserScopedGrant, Guid>>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    // (a) Yalnız CompanyA grant → B reddi.
    [Fact]
    public async Task Only_company_grant_allows_that_company_and_denies_others()
    {
        var s = NewScope();

        using (_currentTenant.Change(s.TenantId))
        {
            await AddGrantAsync(s.UserId, s.CompanyA, null, null, ScopedGrantMode.Grant);

            var access = await ResolveAsync(s.UserId);

            access.CanAccessCompany(s.CompanyA).ShouldBeTrue();
            access.CanAccessCompany(s.CompanyB).ShouldBeFalse();
            access.AllowedCompanyIds.ShouldContain(s.CompanyA);
            access.AllowedCompanyIds.ShouldNotContain(s.CompanyB);
        }
    }

    // (b) Tenant-geneli Grant + CompanyB Deny → B hariç herkes.
    [Fact]
    public async Task Tenant_wide_grant_with_company_deny_excludes_that_company()
    {
        var s = NewScope();

        using (_currentTenant.Change(s.TenantId))
        {
            await AddGrantAsync(s.UserId, null, null, null, ScopedGrantMode.Grant);
            await AddGrantAsync(s.UserId, s.CompanyB, null, null, ScopedGrantMode.Deny);

            var access = await ResolveAsync(s.UserId);

            access.IsTenantWide.ShouldBeTrue();
            access.CanAccessCompany(s.CompanyA).ShouldBeTrue();
            access.CanAccessCompany(s.CompanyB).ShouldBeFalse();
        }
    }

    // (c) CompanyA Deny + BranchA1 Grant → yalnız A1 (branch daha spesifik → company Deny'i EZER).
    [Fact]
    public async Task More_specific_branch_grant_overrides_company_deny()
    {
        var s = NewScope();
        var branchA1 = SimpleGuidGenerator.Instance.Create();
        var branchA2 = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(s.TenantId))
        {
            await AddGrantAsync(s.UserId, s.CompanyA, null, null, ScopedGrantMode.Deny);
            await AddGrantAsync(s.UserId, s.CompanyA, branchA1, null, ScopedGrantMode.Grant);

            var access = await ResolveAsync(s.UserId);

            // Daha spesifik Grant kazanır → A1 erişilir.
            access.CanAccessBranch(s.CompanyA, branchA1).ShouldBeTrue();
            // Company Deny başka şubede geçerli (daha spesifik Grant yok) → A2 reddi.
            access.CanAccessBranch(s.CompanyA, branchA2).ShouldBeFalse();
            // KATI düğüm erişimi: şirket düğümünde en-spesifik kural company Deny → false.
            access.CanAccessCompany(s.CompanyA).ShouldBeFalse();
            // Ama combo daraltma için A1 üzerinden şirket ULAŞILABİLİR.
            access.AllowedBranchIds.ShouldContain(branchA1);
            access.AllowedCompanyIds.ShouldContain(s.CompanyA);
        }
    }

    // (d) Grant yok → boş küme, hiçbir erişim.
    [Fact]
    public async Task No_grants_means_empty_access()
    {
        var s = NewScope();

        using (_currentTenant.Change(s.TenantId))
        {
            var access = await ResolveAsync(s.UserId);

            access.CanAccessCompany(s.CompanyA).ShouldBeFalse();
            access.CanAccessCompany(s.CompanyB).ShouldBeFalse();
            access.IsTenantWide.ShouldBeFalse();
            access.AllowedCompanyIds.ShouldBeEmpty();
            access.AllowedBranchIds.ShouldBeEmpty();
        }
    }

    // (e) İkinci ResolveAsync (cache'ten) aynı sonucu verir.
    [Fact]
    public async Task Second_resolve_is_idempotent_from_cache()
    {
        var s = NewScope();

        using (_currentTenant.Change(s.TenantId))
        {
            await AddGrantAsync(s.UserId, s.CompanyA, null, null, ScopedGrantMode.Grant);

            var first  = await ResolveAsync(s.UserId);
            var second = await ResolveAsync(s.UserId);

            first.CanAccessCompany(s.CompanyA).ShouldBe(second.CanAccessCompany(s.CompanyA));
            first.CanAccessCompany(s.CompanyB).ShouldBe(second.CanAccessCompany(s.CompanyB));
            second.CanAccessCompany(s.CompanyA).ShouldBeTrue();
            second.CanAccessCompany(s.CompanyB).ShouldBeFalse();
        }
    }

    // ── kurulum / yardımcılar ────────────────────────────────────────────────

    private Task AddGrantAsync(Guid userId, Guid? companyId, Guid? branchId, Guid? vaultId, ScopedGrantMode mode)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            // Resolver RoleId/PermissionName'i umursamaz; entity XOR gerektirdiği için bir roleId veriyoruz.
            var roleId = SimpleGuidGenerator.Instance.Create();
            await _grants.InsertAsync(
                new UserScopedGrant(userId, roleId, null, companyId, branchId, vaultId, mode),
                autoSave: true);
        });
    }

    private Task<ScopedAccessSet> ResolveAsync(Guid userId)
    {
        return WithUnitOfWorkAsync(() => _resolver.ResolveAsync(userId));
    }

    private static ScopeIds NewScope()
    {
        return new ScopeIds(
            SimpleGuidGenerator.Instance.Create(),
            SimpleGuidGenerator.Instance.Create(),
            SimpleGuidGenerator.Instance.Create(),
            SimpleGuidGenerator.Instance.Create());
    }

    private sealed record ScopeIds(Guid TenantId, Guid UserId, Guid CompanyA, Guid CompanyB);
}
