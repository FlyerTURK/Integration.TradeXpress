using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Stones;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Company global query filter regresyon ağı: <see cref="ICompanyScoped"/> entity'ler (örnek: Stone)
/// için DbContext seviyesindeki ABP data filter'ının, elle çağrılan
/// <c>CompanyScopedQueryable.WhereCompanyVisible</c> ile BİREBİR aynı görünürlüğü — bu kez unutulması
/// imkânsız şekilde — verdiğini doğrular. Filtre unutulan AppService sorgularında şirketler-arası
/// sızıntıyı yapısal kapatır; konsolide/rapor sorguları <c>DataFilter.Disable&lt;ICompanyScoped&gt;()</c>
/// ile bilinçli açar.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class CompanyScopedFilterTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IRepository<Stone, Guid> _stones;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;

    public CompanyScopedFilterTests()
    {
        _stones         = GetRequiredService<IRepository<Stone, Guid>>();
        _dataFilter     = GetRequiredService<IDataFilter>();
        _currentTenant  = GetRequiredService<ICurrentTenant>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task Filter_hides_other_company_records_when_working_company_is_set()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.CompanyA;

            var visible = await QueryIdsAsync(data);

            // Kendi şirketi + holding-host (CompanyId=null) görünür; DİĞER şirketin kaydı GÖRÜNMEZ.
            visible.ShouldContain(data.StoneA);
            visible.ShouldContain(data.StoneShared);
            visible.ShouldNotContain(data.StoneB);
        }
    }

    [Fact]
    public async Task Host_record_stays_visible_under_working_company()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.CompanyA;

            // HostCatalog deseni: tenant filtresi bilinçli kapalı → host kaydı (TenantId=null)
            // company filtresinden de muaf kalmalı (host‖company görünürlük semantiği).
            using (_dataFilter.Disable<IMultiTenant>())
            {
                var visible = await QueryIdsAsync(data);

                visible.ShouldContain(data.StoneHost);
                visible.ShouldContain(data.StoneA);
                visible.ShouldContain(data.StoneShared);
                visible.ShouldNotContain(data.StoneB);
            }
        }
    }

    [Fact]
    public async Task Filter_matches_WhereCompanyVisible_semantics_exactly()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            // Working şirket dolu VE boş (konsolide) — iki durumda da parite birebir olmalı.
            foreach (var workingCompanyId in new Guid?[] { data.CompanyA, null })
            {
                _companyContext.CompanyId = workingCompanyId;

                List<Guid> filtered;
                using (_dataFilter.Disable<IMultiTenant>())
                {
                    filtered = await QueryIdsAsync(data);
                }

                List<Guid> manual;
                using (_dataFilter.Disable<IMultiTenant>())
                using (_dataFilter.Disable<ICompanyScoped>())
                {
                    manual = await WithUnitOfWorkAsync(async () =>
                    {
                        var stones = await _stones.GetListAsync(s => data.AllIds.Contains(s.Id));
                        return stones
                            .AsQueryable()
                            .WhereCompanyVisible(data.TenantId, workingCompanyId)
                            .Select(s => s.Id)
                            .ToList();
                    });
                }

                filtered.OrderBy(x => x).ShouldBe(manual.OrderBy(x => x));
            }
        }
    }

    [Fact]
    public async Task Disable_filter_shows_all_companies_for_consolidated_queries()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.CompanyA;

            using (_dataFilter.Disable<ICompanyScoped>())
            {
                var visible = await QueryIdsAsync(data);

                // Konsolide sorgu: tenant'ın TÜM şirketleri görünür (tenant filtresi hâlâ açık →
                // host kaydı görünmez; o boyut IMultiTenant filtresinin işi).
                visible.ShouldContain(data.StoneA);
                visible.ShouldContain(data.StoneB);
                visible.ShouldContain(data.StoneShared);
                visible.ShouldNotContain(data.StoneHost);
            }
        }
    }

    [Fact]
    public async Task No_working_company_means_consolidated_visibility()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = null; // şirket seçili değil → konsolide (kısıt yok)

            var visible = await QueryIdsAsync(data);

            visible.ShouldContain(data.StoneA);
            visible.ShouldContain(data.StoneB);
            visible.ShouldContain(data.StoneShared);
        }
    }

    // ── kurulum / yardımcılar ────────────────────────────────────────────────

    /// <summary>
    /// Tek tenant altında iki şirket + tenant-geneli + host global taş grafı kurar.
    /// Kodlar test-başına benzersiz (paylaşılan Sqlite collection DB'si).
    /// </summary>
    private async Task<FilterTestData> SeedAsync()
    {
        _companyContext.CompanyId = null; // auto-stamp devre dışı — CompanyId ctor'dan verilir

        var tenantId = SimpleGuidGenerator.Instance.Create();
        var companyA = SimpleGuidGenerator.Instance.Create();
        var companyB = SimpleGuidGenerator.Instance.Create();
        var suffix   = SimpleGuidGenerator.Instance.Create().ToString("N")[..8].ToUpperInvariant();

        // Host global kayıt (TenantId=null) — testin varsayılan bağlamı host.
        var stoneHost = await WithUnitOfWorkAsync(async () =>
        {
            var host = await _stones.InsertAsync(new Stone($"H{suffix}", $"Host Stone {suffix}"));
            return host.Id;
        });

        Guid stoneA, stoneB, stoneShared;
        using (_currentTenant.Change(tenantId))
        {
            (stoneA, stoneB, stoneShared) = await WithUnitOfWorkAsync(async () =>
            {
                var a      = await _stones.InsertAsync(new Stone($"A{suffix}", $"Company A Stone {suffix}", companyA));
                var b      = await _stones.InsertAsync(new Stone($"B{suffix}", $"Company B Stone {suffix}", companyB));
                var shared = await _stones.InsertAsync(new Stone($"S{suffix}", $"Shared Stone {suffix}"));
                return (a.Id, b.Id, shared.Id);
            });
        }

        return new FilterTestData(tenantId, companyA, companyB, stoneHost, stoneA, stoneB, stoneShared);
    }

    /// <summary>Bu testin tohumladığı taşlardan aktif filtrelerle görünenlerin id listesi.</summary>
    private Task<List<Guid>> QueryIdsAsync(FilterTestData data)
    {
        return WithUnitOfWorkAsync(async () =>
        {
            var stones = await _stones.GetListAsync(s => data.AllIds.Contains(s.Id));
            return stones.Select(s => s.Id).ToList();
        });
    }

    private sealed record FilterTestData(
        Guid TenantId,
        Guid CompanyA,
        Guid CompanyB,
        Guid StoneHost,
        Guid StoneA,
        Guid StoneB,
        Guid StoneShared)
    {
        public List<Guid> AllIds { get; } = new() { StoneHost, StoneA, StoneB, StoneShared };
    }
}
