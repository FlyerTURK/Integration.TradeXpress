using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Organization;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Organization;

/// <summary>
/// Şube-otoriter bilanço birimi devri regresyon ağı (bkz. .claude/rules/financials.md):
/// OrgTreeManager'ın kurduğu/yükselttiği/iyileştirdiği HQ şubeleri şirket base'ini devralmalı;
/// şubenin KENDİ seçtiği birim ise asla ezilmemeli.
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class OrgTreeManagerTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly OrgTreeManager _orgTreeManager;
    private readonly IRepository<Company, Guid> _companies;
    private readonly IRepository<Branch, Guid> _branches;
    private readonly ICurrentTenant _currentTenant;

    public OrgTreeManagerTests()
    {
        _orgTreeManager = GetRequiredService<OrgTreeManager>();
        _companies      = GetRequiredService<IRepository<Company, Guid>>();
        _branches       = GetRequiredService<IRepository<Branch, Guid>>();
        _currentTenant  = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Auto_created_hq_branch_inherits_company_base_currency()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var baseUnitId = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(tenantId))
        {
            var branchId = await WithUnitOfWorkAsync(async () =>
            {
                var company = await _companies.InsertAsync(
                    NewCompany(baseUnitId), autoSave: true);

                var hq = await _orgTreeManager.EnsureHeadquartersBranchAsync(company);
                return hq.Id;
            });

            var branch = await WithUnitOfWorkAsync(() => _branches.GetAsync(branchId));
            branch.IsHeadquarters.ShouldBeTrue();
            branch.BaseCurrencyUnitId.ShouldBe(baseUnitId);
        }
    }

    [Fact]
    public async Task Existing_hq_with_missing_base_currency_is_backfilled_from_company()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var baseUnitId = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(tenantId))
        {
            var branchId = await WithUnitOfWorkAsync(async () =>
            {
                var company = await _companies.InsertAsync(
                    NewCompany(baseUnitId), autoSave: true);

                // Eski davranışın bıraktığı boşluğu taklit et: base'siz HQ şube (devir öncesi kayıtlar).
                var legacyHq = new Branch(
                    company.Id,
                    BranchConsts.DefaultHeadquartersCode,
                    BranchConsts.DefaultHeadquartersName,
                    isHeadquarters: true,
                    displayOrder: 1);
                await _branches.InsertAsync(legacyHq, autoSave: true);

                var hq = await _orgTreeManager.EnsureHeadquartersBranchAsync(company);
                return hq.Id;
            });

            var branch = await WithUnitOfWorkAsync(() => _branches.GetAsync(branchId));
            branch.BaseCurrencyUnitId.ShouldBe(baseUnitId);
        }
    }

    [Fact]
    public async Task Branch_chosen_base_currency_is_never_overwritten_by_company()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var companyBaseId = SimpleGuidGenerator.Instance.Create();
        var branchOwnBaseId = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(tenantId))
        {
            var branchId = await WithUnitOfWorkAsync(async () =>
            {
                var company = await _companies.InsertAsync(
                    NewCompany(companyBaseId), autoSave: true);

                // Şube kendi bilanço birimini seçmiş (şube-otoriter) — HQ değil, yükseltme yolundan geçer.
                var branch = new Branch(
                    company.Id,
                    BranchConsts.DefaultHeadquartersCode,
                    BranchConsts.DefaultHeadquartersName,
                    isHeadquarters: false,
                    displayOrder: 1);
                branch.SetBaseCurrency(branchOwnBaseId);
                await _branches.InsertAsync(branch, autoSave: true);

                var hq = await _orgTreeManager.EnsureHeadquartersBranchAsync(company);
                return hq.Id;
            });

            var branch = await WithUnitOfWorkAsync(() => _branches.GetAsync(branchId));
            branch.IsHeadquarters.ShouldBeTrue();
            branch.BaseCurrencyUnitId.ShouldBe(branchOwnBaseId);
        }
    }

    /// <summary>Test-başına benzersiz kodlu şirket kurar (paylaşılan Sqlite collection DB'si).</summary>
    private static Company NewCompany(Guid baseCurrencyUnitId)
    {
        var suffix = SimpleGuidGenerator.Instance.Create().ToString("N")[..8].ToUpperInvariant();
        return new Company(
            $"C{suffix}",
            $"Org Tree Company {suffix}",
            "TR",
            baseCurrencyUnitId);
    }
}
