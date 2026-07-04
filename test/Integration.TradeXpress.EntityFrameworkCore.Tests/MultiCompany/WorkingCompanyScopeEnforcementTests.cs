using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Authorization;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Faz 4 Adım 2 — working-context YETKİ zorlaması güvenlik ağı. İki katman doğrulanır:
/// <para>(1) SAF kural <see cref="WorkingCompanyScope"/> sahte/yetkisiz seçimi izinli kümeye indirger
/// (yetkisiz → ilk izinli; hiç yok → <see cref="Guid.Empty"/> sentinel; ASLA null=konsolide).</para>
/// <para>(2) UÇTAN UCA: kullanıcı yalnız CompanyA'ya grant'lıyken CompanyB seçilse bile efektif şirket A'ya
/// düşer → CompanyB'nin <see cref="ICompanyOwned"/> (Account) kayıtları GÖRÜNMEZ; grant yoksa sentinel tüm
/// owned kayıtları gizler ama host/paylaşılan katalog görünür kalır (null-permissive tuzağına düşülmez).</para>
///
/// <para>Provider sınıfı (<c>WorkingCompanyContextProvider</c>) Blazor.Client (WASM) projesinde olduğundan
/// EFCore.Tests'ten doğrudan referanslanamaz; bu test onun KARARINI birebir doğrular: AYNI saf kural +
/// GERÇEK <see cref="IScopedGrantResolver"/> + GERÇEK DbContext company filtresi. Provider'ın efektif id'yi
/// nasıl hesapladığı = burada hesaplanan; DbContext'e o değer <see cref="TestCompanyContextProvider"/> ile
/// verilir.</para>
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class WorkingCompanyScopeEnforcementTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IScopedGrantResolver _resolver;
    private readonly IRepository<UserScopedGrant, Guid> _grants;
    private readonly IRepository<Account, Guid> _accounts;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;
    private readonly TestCompanyContextProvider _companyContext;

    public WorkingCompanyScopeEnforcementTests()
    {
        _resolver       = GetRequiredService<IScopedGrantResolver>();
        _grants         = GetRequiredService<IRepository<UserScopedGrant, Guid>>();
        _accounts       = GetRequiredService<IRepository<Account, Guid>>();
        _seeder         = GetRequiredService<VoucherTestDataSeeder>();
        _currentTenant  = GetRequiredService<ICurrentTenant>();
        _dataFilter     = GetRequiredService<IDataFilter>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    // ── (1) saf kural (DB'siz) ─────────────────────────────────────────────────

    [Fact]
    public void Allowed_selection_passes_through()
    {
        var a = SimpleGuidGenerator.Instance.Create();
        var b = SimpleGuidGenerator.Instance.Create();
        var allowed = new List<Guid> { a, b };

        WorkingCompanyScope.ResolveEffectiveCompanyId(a, allowed).ShouldBe(a);
        WorkingCompanyScope.IsUnauthorizedSelection(a, allowed).ShouldBeFalse();
    }

    [Fact]
    public void Unauthorized_selection_falls_to_first_allowed_not_null()
    {
        var a = SimpleGuidGenerator.Instance.Create();
        var b = SimpleGuidGenerator.Instance.Create();
        var allowed = new List<Guid> { a };

        // Sahte B seçimi → ilk izinli A (null DEĞİL — konsolide/ters-güvenlik önlenir).
        WorkingCompanyScope.ResolveEffectiveCompanyId(b, allowed).ShouldBe(a);
        WorkingCompanyScope.IsUnauthorizedSelection(b, allowed).ShouldBeTrue();
    }

    [Fact]
    public void No_allowed_company_yields_empty_sentinel_not_null()
    {
        var b = SimpleGuidGenerator.Instance.Create();
        var allowed = new List<Guid>();

        WorkingCompanyScope.ResolveEffectiveCompanyId(b, allowed).ShouldBe(Guid.Empty);
        WorkingCompanyScope.ResolveEffectiveCompanyId(null, allowed).ShouldBe(Guid.Empty);
        WorkingCompanyScope.IsUnauthorizedSelection(b, allowed).ShouldBeTrue();
    }

    // ── (2) uçtan uca (gerçek resolver + gerçek filtre) ────────────────────────

    [Fact]
    public async Task Fake_company_selection_falls_to_granted_company_and_hides_foreign_owned_records()
    {
        var data = await SeedAsync();
        var userId = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(data.TenantId))
        {
            // Kullanıcı YALNIZ CompanyA'ya grant'lı.
            await AddCompanyGrantAsync(userId, data.CompanyA);
            var allowed = await ResolveAllowedCompaniesAsync(userId);
            allowed.ShouldBe(new[] { data.CompanyA });

            // SAHTE seçim: CompanyB. Provider kuralı (WorkingCompanyScope) → efektif A.
            var effective = WorkingCompanyScope.ResolveEffectiveCompanyId(data.CompanyB, allowed);
            effective.ShouldBe(data.CompanyA);

            // Efektif id filtreye verilir → CompanyB'nin owned kaydı GÖRÜNMEZ (sahte seçim filtreye ulaşmadı).
            _companyContext.CompanyId = effective;
            var visible = await QueryAccountIdsAsync(data);

            visible.ShouldContain(data.AccountA);
            visible.ShouldNotContain(data.AccountB);
        }
    }

    [Fact]
    public async Task No_grant_sentinel_hides_all_owned_but_keeps_host_visible()
    {
        var data = await SeedAsync();
        var userId = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(data.TenantId))
        {
            // Grant YOK → izinli şirket yok → sentinel Guid.Empty.
            var allowed = await ResolveAllowedCompaniesAsync(userId);
            allowed.ShouldBeEmpty();

            var effective = WorkingCompanyScope.ResolveEffectiveCompanyId(data.CompanyA, allowed);
            effective.ShouldBe(Guid.Empty);

            _companyContext.CompanyId = effective;

            // Owned (Account) TÜMÜ gizli — Guid.Empty null DEĞİL → filtre aktif kalır, konsolide-permissive'e düşmez.
            var ownedVisible = await QueryAccountIdsAsync(data);
            ownedVisible.ShouldNotContain(data.AccountA);
            ownedVisible.ShouldNotContain(data.AccountB);

            // Host kaydı (TenantId=null) muaf → tenant filtresi kapalıyken görünür kalır (katalog sızıntısı yok).
            using (_dataFilter.Disable<IMultiTenant>())
            {
                var hostVisible = await WithUnitOfWorkAsync(async () =>
                {
                    var list = await _accounts.GetListAsync(x => x.Id == data.HostAccountId);
                    return list.Select(x => x.Id).ToList();
                });
                hostVisible.ShouldContain(data.HostAccountId);
            }
        }
    }

    // ── kurulum / yardımcılar ────────────────────────────────────────────────

    private Task AddCompanyGrantAsync(Guid userId, Guid companyId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            // Resolver RoleId/PermissionName'i umursamaz; saf coğrafi grant (ikisi de null) yeter.
            await _grants.InsertAsync(
                new UserScopedGrant(userId, null, null, companyId, null, null, ScopedGrantMode.Grant),
                autoSave: true);
        });
    }

    /// <summary>Gerçek resolver'dan kullanıcının izinli şirketlerini (sıralı liste) çözer — provider'ın
    /// <see cref="IWorkingContextService.AllowedCompanyIds"/>'inin sunucu karşılığı.</summary>
    private Task<List<Guid>> ResolveAllowedCompaniesAsync(Guid userId)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var access = await _resolver.ResolveAsync(userId);
            return access.AllowedCompanyIds.OrderBy(x => x).ToList();
        });
    }

    private Task<List<Guid>> QueryAccountIdsAsync(AccountScopeData data)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var list = await _accounts.GetListAsync(a => data.AllIds.Contains(a.Id));
            return list.Select(a => a.Id).ToList();
        });
    }

    /// <summary>Tek tenant + iki tam şirket grafı (A/B, VoucherTestDataSeeder) + host hesabı (TenantId=null).</summary>
    private async Task<AccountScopeData> SeedAsync()
    {
        _companyContext.CompanyId = null; // seed sırasında context yok

        var tenantId = SimpleGuidGenerator.Instance.Create();
        var suffix = SimpleGuidGenerator.Instance.Create().ToString("N")[..8].ToUpperInvariant();

        VoucherTestData a, b;
        using (_currentTenant.Change(tenantId))
        {
            (a, b) = await WithUnitOfWorkAsync(async () =>
            {
                var ga = await _seeder.SeedCompanyGraphAsync($"WA{suffix[..4]}");
                var gb = await _seeder.SeedCompanyGraphAsync($"WB{suffix[..4]}");
                return (ga, gb);
            });
        }

        // Host hesabı (TenantId=null — tenant değişmeden); CompanyId rastgele (working şirketle uyuşmaz).
        var hostAccountId = await WithUnitOfWorkAsync(async () =>
        {
            var host = await _accounts.InsertAsync(
                new Account(
                    SimpleGuidGenerator.Instance.Create(),
                    $"WH{suffix[..4]}",
                    "Working Host Account",
                    a.TryUnitId,
                    a.TryUnitId),
                autoSave: true);
            return host.Id;
        });

        return new AccountScopeData(tenantId, a.CompanyId, b.CompanyId, a.AccountId, b.AccountId, hostAccountId);
    }

    private sealed record AccountScopeData(
        Guid TenantId,
        Guid CompanyA,
        Guid CompanyB,
        Guid AccountA,
        Guid AccountB,
        Guid HostAccountId)
    {
        public List<Guid> AllIds { get; } = new() { AccountA, AccountB, HostAccountId };
    }
}
