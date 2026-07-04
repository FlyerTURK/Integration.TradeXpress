using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Organization;
using Integration.TradeXpress.Vaults;
using Shouldly;
using Volo.Abp.Data;
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
    private readonly IRepository<Vault, Guid> _vaults;
    private readonly TestCompanyContextProvider _companyContext;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;

    public OrgTreeManagerTests()
    {
        _orgTreeManager = GetRequiredService<OrgTreeManager>();
        _companies      = GetRequiredService<IRepository<Company, Guid>>();
        _branches       = GetRequiredService<IRepository<Branch, Guid>>();
        _vaults         = GetRequiredService<IRepository<Vault, Guid>>();
        _companyContext = GetRequiredService<TestCompanyContextProvider>();
        _dataFilter     = GetRequiredService<IDataFilter>();
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

    /// <summary>
    /// Canlı onboarding bug regresyonu: yeni tenant admin'i henüz şirket seçmemişken izinli-şirket kümesi
    /// boştur → <c>WorkingCompanyContextProvider</c> <see cref="Guid.Empty"/> SENTINEL döndürür (null DEĞİL).
    /// <see cref="Vault"/> (<c>ICompanyOwned</c>) görünürlük filtresi sentinel'de TÜM kasaları gizler
    /// (permissive yalnız CurrentCompanyId=null iken). Sonuç: <see cref="OrgTreeManager.EnsureDefaultVaultAsync"/>
    /// mevcut kasayı GÖREMEZ → yeniden "KASA" insert etmeye çalışır → benzersizlik (TenantId,BranchId,Code)
    /// çakışması → BusinessException. Şube <c>ICompanyOwned</c> DEĞİL → görünür kalır; canlıda gözlenen
    /// "şube oluştu ama kasa oluşmadı + hata toast'ı" tam olarak budur. Fix: org kurulumu şubenin KENDİ
    /// şirketine scope'lanır → sentinel filtresi bypass edilir, idempotency korunur.
    /// </summary>
    [Fact]
    public async Task Default_vault_stays_idempotent_when_working_context_company_is_empty_sentinel()
    {
        var tenantId = SimpleGuidGenerator.Instance.Create();
        var baseUnitId = SimpleGuidGenerator.Instance.Create();

        using (_currentTenant.Change(tenantId))
        {
            _companyContext.CompanyId = null; // normal kurulum → ilk (varsayılan) kasa oluşur
            var branch = await WithUnitOfWorkAsync(async () =>
            {
                var company = await _companies.InsertAsync(NewCompany(baseUnitId), autoSave: true);
                return await _orgTreeManager.EnsureHeadquartersBranchAsync(company);
            });

            // Onboarding anı: working-context şirketi yok → sentinel (izinli küme boş).
            _companyContext.CompanyId = Guid.Empty;

            // İkinci idempotent çağrı sentinel altında: mevcut kasa gizlenmemeli → throw YOK, ikinci kasa YOK.
            var again = await WithUnitOfWorkAsync(() => _orgTreeManager.EnsureDefaultVaultAsync(branch));
            again.ShouldNotBeNull();

            // Şubede tam olarak tek kasa (sentinel filtresini Disable ile bypass ederek say).
            var vaultCount = await WithUnitOfWorkAsync(async () =>
            {
                using (_dataFilter.Disable<ICompanyScoped>())
                {
                    return await _vaults.CountAsync(v => v.BranchId == branch.Id);
                }
            });
            vaultCount.ShouldBe(1);
        }
    }

    /// <summary>Test-başına benzersiz kodlu şirket kurar (paylaşılan Sqlite collection DB'si).
    /// CountryId id-only referanstır (DB FK yok) → org-ağacı senaryosunda sentetik id yeterli.</summary>
    private static Company NewCompany(Guid baseCurrencyUnitId)
    {
        var suffix = SimpleGuidGenerator.Instance.Create().ToString("N")[..8].ToUpperInvariant();
        return new Company(
            $"C{suffix}",
            $"Org Tree Company {suffix}",
            SimpleGuidGenerator.Instance.Create(),
            baseCurrencyUnitId);
    }
}
