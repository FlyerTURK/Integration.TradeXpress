using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.EntityFrameworkCore;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Account çok-şirket GÜVENLİK SINIRI regresyon ağı (Faz 0 — <see cref="ICompanyOwned"/> örneği).
/// <see cref="ICompanyScoped"/> (Stone/Jewelry görünüm) filtresinden farklı olarak Account bir güvenlik
/// sınırıdır: yabancı şirketin hesabı working-context'te YOKMUŞ gibi davranır (okuma/güncelleme/silme
/// EntityNotFound); yazma yolları fail-closed (sahte CompanyId reddedilir). Konsolide (context yok)
/// PERMISSIVE kalır; host kaydı (TenantId=null) muaf. IDataFilter anahtarı <see cref="ICompanyScoped"/>
/// ile PAYLAŞILIR (iki marker tek anahtar).
/// </summary>
[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class AccountCompanyScopeTests : TradeXpressEntityFrameworkCoreTestBase
{
    private readonly IAccountAppService _accountAppService;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly VoucherTestDataSeeder _seeder;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDataFilter _dataFilter;
    private readonly TestCompanyContextProvider _companyContext;

    public AccountCompanyScopeTests()
    {
        _accountAppService    = GetRequiredService<IAccountAppService>();
        _accountRepository    = GetRequiredService<IRepository<Account, Guid>>();
        _subAccountRepository = GetRequiredService<IRepository<SubAccount, Guid>>();
        _seeder               = GetRequiredService<VoucherTestDataSeeder>();
        _currentTenant        = GetRequiredService<ICurrentTenant>();
        _dataFilter           = GetRequiredService<IDataFilter>();
        _companyContext       = GetRequiredService<TestCompanyContextProvider>();
    }

    [Fact]
    public async Task Foreign_company_account_get_behaves_as_not_found()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            await Should.ThrowAsync<EntityNotFoundException>(
                () => _accountAppService.GetAsync(data.Foreign.AccountId));

            // Kontrol grubu: kendi şirketinin hesabı normal açılır.
            (await _accountAppService.GetAsync(data.Mine.AccountId)).Id.ShouldBe(data.Mine.AccountId);
        }
    }

    [Fact]
    public async Task Foreign_company_account_update_behaves_as_not_found()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            await Should.ThrowAsync<EntityNotFoundException>(
                () => _accountAppService.UpdateAsync(data.Foreign.AccountId, BuildUpdate(data.Mine.TryUnitId)));
        }
    }

    [Fact]
    public async Task Foreign_company_account_delete_behaves_as_not_found_and_leaves_it_intact()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            await Should.ThrowAsync<EntityNotFoundException>(
                () => _accountAppService.DeleteAsync(data.Foreign.AccountId));

            // IDOR ağı: silme başarısız olmalı VE yabancı hesabın alt hesapları YIKILMAMALI
            // (doğrulama, alt-hesap cascade silmesinden ÖNCE fail-fast eder).
            _companyContext.CompanyId = data.Foreign.CompanyId;
            (await _accountAppService.GetAsync(data.Foreign.AccountId)).Id.ShouldBe(data.Foreign.AccountId);

            var subExists = await WithUnitOfWorkAsync(
                () => _subAccountRepository.AnyAsync(s => s.Id == data.Foreign.SubAccountId));
            subExists.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Create_with_foreign_or_missing_company_context_is_rejected()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            // Sahte CompanyId (yabancı şirket) working-context ile uyuşmaz → reddedilir.
            _companyContext.CompanyId = data.Mine.CompanyId;
            (await Should.ThrowAsync<BusinessException>(
                    () => _accountAppService.CreateAsync(BuildCreate(data.Foreign.CompanyId, data.Mine.TryUnitId))))
                .Code.ShouldBe("TradeXpress:Account:CompanyContextMismatch");

            // Working-context yokken yazma fail-closed (konsolide okuma serbest ama yazma değil).
            _companyContext.CompanyId = null;
            (await Should.ThrowAsync<BusinessException>(
                    () => _accountAppService.CreateAsync(BuildCreate(data.Mine.CompanyId, data.Mine.TryUnitId))))
                .Code.ShouldBe("TradeXpress:Account:CompanyContextRequired");
        }
    }

    [Fact]
    public async Task Create_with_duplicate_code_in_same_company_gives_friendly_error()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            // İlk kayıt: "NEWACC" oluşur.
            await WithUnitOfWorkAsync(() => _accountAppService.CreateAsync(BuildCreate(data.Mine.CompanyId, data.Mine.TryUnitId)));

            // Aynı şirkette aynı kod tekrar → ham DB unique çakışması DEĞİL, dostane BusinessException.
            (await Should.ThrowAsync<BusinessException>(
                    () => WithUnitOfWorkAsync(() => _accountAppService.CreateAsync(BuildCreate(data.Mine.CompanyId, data.Mine.TryUnitId)))))
                .Code.ShouldBe("TradeXpress:Account:CodeAlreadyExists");
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
                var list = await _accountRepository.GetListAsync(
                    a => a.Id == data.Mine.AccountId || a.Id == data.Foreign.AccountId);
                return list.Select(a => a.Id).ToList();
            });

            visible.ShouldContain(data.Mine.AccountId);
            visible.ShouldContain(data.Foreign.AccountId);
        }
    }

    [Fact]
    public async Task Host_account_stays_visible_under_working_company()
    {
        var data = await SeedAsync();

        using (_currentTenant.Change(data.TenantId))
        {
            _companyContext.CompanyId = data.Mine.CompanyId;

            // Host kaydı (TenantId=null) tenant filtresi bilinçli kapalıyken company filtresinden de MUAF
            // kalmalı — CompanyId working şirketle uyuşmasa bile görünür (host-muafiyet kolu).
            using (_dataFilter.Disable<IMultiTenant>())
            {
                var visible = await WithUnitOfWorkAsync(async () =>
                {
                    var list = await _accountRepository.GetListAsync(a => a.Id == data.HostAccountId);
                    return list.Select(a => a.Id).ToList();
                });

                visible.ShouldContain(data.HostAccountId);
            }
        }
    }

    // ── kurulum / yardımcılar ────────────────────────────────────────────────

    /// <summary>Tek tenant altında iki şirket grafı (mine/foreign) + host hesabı (TenantId=null) kurar.
    /// Hesaplar TENANT altında seed'lenir (aksi hâlde TenantId=null olur → host-muafiyetiyle sızmaz test etmezdik).</summary>
    private async Task<AccountScopeData> SeedAsync()
    {
        _companyContext.CompanyId = null; // seed sırasında context yok

        var tenantId = SimpleGuidGenerator.Instance.Create();

        VoucherTestData mine, foreign;
        using (_currentTenant.Change(tenantId))
        {
            (mine, foreign) = await WithUnitOfWorkAsync(async () =>
            {
                var m = await _seeder.SeedCompanyGraphAsync("AS1");
                var f = await _seeder.SeedCompanyGraphAsync("AS2");
                return (m, f);
            });
        }

        // Host hesabı (TenantId=null — tenant değişmeden eklenir); CompanyId rastgele (working şirketle uyuşmaz).
        var hostAccountId = await WithUnitOfWorkAsync(async () =>
        {
            var host = await _accountRepository.InsertAsync(
                new Account(
                    SimpleGuidGenerator.Instance.Create(),
                    "HOSTACC",
                    "Host Account",
                    mine.TryUnitId,
                    mine.TryUnitId),
                autoSave: true);
            return host.Id;
        });

        return new AccountScopeData(tenantId, mine, foreign, hostAccountId);
    }

    private static AccountCreateDto BuildCreate(Guid companyId, Guid unitId)
    {
        return new AccountCreateDto
        {
            CompanyId             = companyId,
            Code                  = "NEWACC",
            Name                  = "New Account",
            BalanceCurrencyUnitId = unitId,
            LimitUnitId           = unitId,
            Limit                 = 0m,
        };
    }

    private static AccountUpdateDto BuildUpdate(Guid unitId)
    {
        return new AccountUpdateDto
        {
            Code                  = "RENAMEDACC",   // Code artık UpdateDto'da zorunlu (kod düzenlenebilir ürün kuralı)
            Name                  = "Renamed Account",
            BalanceCurrencyUnitId = unitId,
            LimitUnitId           = unitId,
            Limit                 = 0m,
            IsActive              = true,
        };
    }

    private sealed record AccountScopeData(
        Guid TenantId,
        VoucherTestData Mine,
        VoucherTestData Foreign,
        Guid HostAccountId);
}
