using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// SubAccount çok-şirket GÜVENLİK SINIRI regresyon ağı — <see cref="SubAccount"/> artık
/// <see cref="ICompanyOwned"/> (CompanyId parent hesaptan DENORMALİZE). Daha önce SubAccount'ta company
/// kolonu YOKTU → doğrudan <c>DeleteAsync</c> cross-company IDOR açıktı (cascade sırasında yabancı alt
/// hesaplar silinebilirdi). Bu ağ: yabancı şirketin alt hesabı working-context'te YOKMUŞ gibi davranır
/// (Get/Update/Delete → EntityNotFound), delete başarısız olunca yabancı satır YIKILMAZ ve CompanyId
/// client'tan değil yalnız GÖRÜNÜR parent hesaptan türetilir (forgery kapalı).
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class SubAccountCompanyScopeTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly ISubAccountAppService _subAccountAppService;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly ICurrentTenant _currentTenant;
    private readonly TestCompanyContextProvider _companyContext;

    public SubAccountCompanyScopeTests()
    {
        _subAccountAppService = GetRequiredService<ISubAccountAppService>();
        _subAccountRepository = GetRequiredService<IRepository<SubAccount, Guid>>();
        _seeder               = GetRequiredService<VoucherTestDataSeeder>();
        _currentTenant        = GetRequiredService<ICurrentTenant>();
        _companyContext       = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task Foreign_company_subaccount_get_behaves_as_not_found()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            await Should.ThrowAsync<EntityNotFoundException>(
                () => _subAccountAppService.GetAsync(data.Foreign.SubAccountId));

            // Kontrol grubu: kendi şirketinin alt hesabı normal açılır.
            (await _subAccountAppService.GetAsync(data.Mine.SubAccountId)).Id.ShouldBe(data.Mine.SubAccountId);
        }
    }

    [Fact]
    public async Task Foreign_company_subaccount_update_behaves_as_not_found()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            await Should.ThrowAsync<EntityNotFoundException>(
                () => _subAccountAppService.UpdateAsync(data.Foreign.SubAccountId, BuildUpdate()));
        }
    }

    [Fact]
    public async Task Foreign_company_subaccount_delete_behaves_as_not_found_and_leaves_it_intact()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            // IDOR ağı (kapatılan açık): yabancı şirketin alt hesabı silinemez.
            await Should.ThrowAsync<EntityNotFoundException>(
                () => _subAccountAppService.DeleteAsync(data.Foreign.SubAccountId));

            // Silme başarısız olmalı VE yabancı satır YIKILMAMALI — doğrulamada yabancı şirket
            // context'ine geç (aksi hâlde satır zaten filtreyle GİZLİ, "silinmiş" gibi görünürdü).
            _companyContext.CompanyId = data.Foreign.CompanyId;
            var stillExists = await WithUnitOfWorkAsync(
                () => _subAccountRepository.AnyAsync(s => s.Id == data.Foreign.SubAccountId));
            stillExists.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Create_under_foreign_account_is_rejected_and_own_derives_company()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            // Forgery kapalı: yabancı şirketin hesabı company-filter altında görünmez → türetilecek
            // CompanyId'ye erişilemez → EntityNotFound (client CompanyId zaten göndermez, parent'tan türer).
            await Should.ThrowAsync<EntityNotFoundException>(
                () => _subAccountAppService.CreateAsync(BuildCreate(data.Foreign.AccountId)));

            // Kendi hesabı altında oluşturma → CompanyId parent hesaptan (mine) denormalize edilir.
            var created = await _subAccountAppService.CreateAsync(BuildCreate(data.Mine.AccountId));
            var entity = await WithUnitOfWorkAsync(() => _subAccountRepository.GetAsync(created.Id));
            entity.CompanyId.ShouldBe(data.Mine.CompanyId);
        }
    }

    [Fact]
    public async Task Consolidated_context_sees_all_companies()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = null; // şirket seçili değil → konsolide (kısıt yok)

            var visible = await WithUnitOfWorkAsync(async () =>
            {
                var list = await _subAccountRepository.GetListAsync(
                    s => s.Id == data.Mine.SubAccountId || s.Id == data.Foreign.SubAccountId);
                return list;
            });

            visible.Count.ShouldBe(2);
        }
    }

    // ── kurulum / yardımcılar ────────────────────────────────────────────────

    private async Task<SubAccountScopeData> SeedAsync()
    {
        _companyContext.CompanyId = null; // seed sırasında context yok

        var tenantId = SimpleGuidGenerator.Instance.Create();

        VoucherTestData mine, foreign;
        using (_currentTenant.Change(tenantId))
        {
            (mine, foreign) = await WithUnitOfWorkAsync(async () =>
            {
                var m = await _seeder.SeedCompanyGraphAsync("SS1");
                var f = await _seeder.SeedCompanyGraphAsync("SS2");
                return (m, f);
            });
        }

        return new SubAccountScopeData(tenantId, mine, foreign);
    }

    private static SubAccountCreateDto BuildCreate(Guid accountId)
    {
        return new SubAccountCreateDto
        {
            AccountId = accountId,
            BranchId  = null,
            Code      = "NEWSUB",
            Name      = "New Sub Account",
        };
    }

    private static SubAccountUpdateDto BuildUpdate()
    {
        return new SubAccountUpdateDto
        {
            Code     = "RENAMED",   // Code artık UpdateDto'da zorunlu (kod düzenlenebilir ürün kuralı)
            Name     = "Renamed Sub Account",
            IsActive = true,
        };
    }

    private sealed record SubAccountScopeData(
        Guid TenantId,
        VoucherTestData Mine,
        VoucherTestData Foreign);
}
