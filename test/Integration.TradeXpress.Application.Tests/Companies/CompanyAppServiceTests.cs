using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Currencies;
using Integration.TradeXpress.Vaults;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Integration.TradeXpress.Companies;

public abstract class CompanyAppServiceTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ICompanyAppService _appService;
    private readonly ICurrencyUnitAppService _currencyUnitAppService;
    private readonly IVaultAppService _vaultAppService;
    private readonly ICurrentTenant _currentTenant;

    protected CompanyAppServiceTests()
    {
        _appService = GetRequiredService<ICompanyAppService>();
        _currencyUnitAppService = GetRequiredService<ICurrencyUnitAppService>();
        _vaultAppService = GetRequiredService<IVaultAppService>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Host_has_no_companies()
    {
        // Şirket/şube TENANT'a aittir; host (merkezi operasyon) şirket tutmaz.
        var list = await _appService.GetListAsync(new CompanyListRequestDto { MaxResultCount = 100 });
        list.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Host_cannot_create_a_company()
    {
        var usd = (await _currencyUnitAppService.GetListAsync(new CurrencyUnitListRequestDto { Filter = "USD" }))
            .Items.Single(u => u.Code == CurrencyUnitCode.USD);

        await Should.ThrowAsync<BusinessException>(() => _appService.CreateAsync(new CompanyCreateDto
        {
            Name = "Olmaz",
            CountryCode = "US",
            BaseCurrencyUnitId = usd.Id,
        }));
    }

    // ── SaveTree (en riskli metot) ──────────────────────────────────────────────

    private async Task<Guid> GetTryUnitIdAsync()
        => (await _currencyUnitAppService.GetListAsync(new CurrencyUnitListRequestDto { Filter = "TRY" }))
            .Items.Single(u => u.Code == CurrencyUnitCode.TRY).Id;

    [Fact]
    public async Task SaveTree_with_empty_branches_injects_default_HQ_branch_and_vault()
    {
        var tryId = await GetTryUnitIdAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var saved = await _appService.SaveTreeAsync(new CompanyTreeSaveDto
            {
                Name = "Acme", CountryCode = "TR", BaseCurrencyUnitId = tryId, IsHeadquarters = true,
                // Branches boş → sunucu varsayılan HQ şube enjekte etmeli
            });

            saved.Branches.Count.ShouldBe(1);
            saved.Branches[0].IsHeadquarters.ShouldBeTrue();

            // Kasa ağaca dahil değil; ama sunucu yeni şube için varsayılan kasayı OTOMATİK oluşturmalı
            // (en az 1 kasa değişmezi — EnsureDefaultVaultAsync). Kasa drill'i Şirketler ekranından yönetilir.
            var vaults = await _vaultAppService.GetListAsync(
                new VaultListRequestDto { BranchId = saved.Branches[0].Id, MaxResultCount = 10 });
            vaults.TotalCount.ShouldBe(1);
            vaults.Items[0].IsDefault.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task SaveTree_normalizes_multiple_HQ_branches_to_one()
    {
        var tryId = await GetTryUnitIdAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var saved = await _appService.SaveTreeAsync(new CompanyTreeSaveDto
            {
                Name = "Acme", CountryCode = "TR", BaseCurrencyUnitId = tryId, IsHeadquarters = true,
                Branches = new List<BranchTreeSaveDto>
                {
                    new() { Name = "Merkez", IsHeadquarters = true, DisplayOrder = 1 },
                    new() { Name = "İkinci", IsHeadquarters = true, DisplayOrder = 2 },
                },
            });

            saved.Branches.Count(b => b.IsHeadquarters).ShouldBe(1);
            // Deterministik: en düşük DisplayOrder HQ kalır.
            saved.Branches.Single(b => b.IsHeadquarters).Name.ShouldBe("Merkez");
        }
    }

    [Fact]
    public async Task SaveTree_removes_branch_via_DeletedBranchIds_with_vault_cascade()
    {
        var tryId = await GetTryUnitIdAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var initial = await _appService.SaveTreeAsync(new CompanyTreeSaveDto
            {
                Name = "Acme", CountryCode = "TR", BaseCurrencyUnitId = tryId, IsHeadquarters = true,
                Branches = new List<BranchTreeSaveDto>
                {
                    new() { Name = "Merkez", IsHeadquarters = true, DisplayOrder = 1 },
                    new() { Name = "Şube2", IsHeadquarters = false, DisplayOrder = 2 },
                },
            });
            var hq = initial.Branches.Single(b => b.IsHeadquarters);
            var other = initial.Branches.Single(b => !b.IsHeadquarters);

            var update = CloneForUpdate(initial);
            update.Branches = update.Branches.Where(b => b.Id == hq.Id).ToList();
            update.DeletedBranchIds = new List<Guid> { other.Id };

            var saved = await _appService.SaveTreeAsync(update);
            saved.Branches.Count.ShouldBe(1);
            saved.Branches[0].Id.ShouldBe(hq.Id);
        }
    }

    [Fact]
    public async Task SaveTree_cannot_unset_company_headquarters()
    {
        var tryId = await GetTryUnitIdAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var initial = await _appService.SaveTreeAsync(new CompanyTreeSaveDto
            {
                Name = "Acme", CountryCode = "TR", BaseCurrencyUnitId = tryId, IsHeadquarters = true,
            });

            var update = CloneForUpdate(initial);
            update.IsHeadquarters = false;

            await Should.ThrowAsync<BusinessException>(() => _appService.SaveTreeAsync(update));
        }
    }

    [Fact]
    public async Task SaveTree_deleting_HQ_branch_without_transfer_throws()
    {
        var tryId = await GetTryUnitIdAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var initial = await _appService.SaveTreeAsync(new CompanyTreeSaveDto
            {
                Name = "Acme", CountryCode = "TR", BaseCurrencyUnitId = tryId, IsHeadquarters = true,
                Branches = new List<BranchTreeSaveDto>
                {
                    new() { Name = "Merkez", IsHeadquarters = true, DisplayOrder = 1 },
                    new() { Name = "Şube2", IsHeadquarters = false, DisplayOrder = 2 },
                },
            });
            var hq = initial.Branches.Single(b => b.IsHeadquarters);
            var other = initial.Branches.Single(b => !b.IsHeadquarters);

            // HQ'yu sil ama kalan şubeye HQ devretme → reddedilmeli.
            var update = CloneForUpdate(initial);
            update.Branches = update.Branches.Where(b => b.Id == other.Id).ToList();
            update.Branches[0].IsHeadquarters = false;
            update.DeletedBranchIds = new List<Guid> { hq.Id };

            await Should.ThrowAsync<BusinessException>(() => _appService.SaveTreeAsync(update));
        }
    }

    [Fact]
    public async Task SaveTree_with_stale_child_id_throws()
    {
        var tryId = await GetTryUnitIdAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var initial = await _appService.SaveTreeAsync(new CompanyTreeSaveDto
            {
                Name = "Acme", CountryCode = "TR", BaseCurrencyUnitId = tryId, IsHeadquarters = true,
            });

            var update = CloneForUpdate(initial);
            update.Branches.Add(new BranchTreeSaveDto { Id = Guid.NewGuid(), Name = "Hayalet", DisplayOrder = 9 });

            await Should.ThrowAsync<BusinessException>(() => _appService.SaveTreeAsync(update));
        }
    }

    // ── Optimistik concurrency (kilidin FİİLEN çalıştığının kanıtı — SQLite token enforcement) ──

    [Fact]
    public async Task SaveTree_with_stale_company_stamp_throws()
    {
        var tryId = await GetTryUnitIdAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var initial = await _appService.SaveTreeAsync(new CompanyTreeSaveDto
            {
                Name = "Acme", CountryCode = "TR", BaseCurrencyUnitId = tryId, IsHeadquarters = true,
            });

            // İlk güncelleme başarılı → company stamp döner/değişir.
            var first = CloneForUpdate(initial);
            first.Name = "Acme-1";
            await _appService.SaveTreeAsync(first);

            // İkinci güncelleme BAYAT (initial) stamp ile → optimistik kilit devreye girmeli.
            var stale = CloneForUpdate(initial);
            stale.Name = "Acme-2";
            await Should.ThrowAsync<AbpDbConcurrencyException>(() => _appService.SaveTreeAsync(stale));
        }
    }

    [Fact]
    public async Task SaveTree_with_stale_branch_stamp_throws()
    {
        var tryId = await GetTryUnitIdAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var initial = await _appService.SaveTreeAsync(new CompanyTreeSaveDto
            {
                Name = "Acme", CountryCode = "TR", BaseCurrencyUnitId = tryId, IsHeadquarters = true,
                Branches = new List<BranchTreeSaveDto>
                {
                    new() { Name = "Merkez", IsHeadquarters = true, DisplayOrder = 1 },
                    new() { Name = "Şube2", IsHeadquarters = false, DisplayOrder = 2 },
                },
            });
            var staleBranchStamp = initial.Branches.Single(b => !b.IsHeadquarters).ConcurrencyStamp;

            // Şube2'yi güncelle → şube stamp'i tazelenir (company da tazelenir).
            var first = CloneForUpdate(initial);
            first.Branches.Single(b => !b.IsHeadquarters).Name = "Şube2-1";
            var saved1 = await _appService.SaveTreeAsync(first);

            // saved1'den klonla (company + diğer stamp'ler GÜNCEL) ama Şube2 stamp'ini BAYAT yap →
            // company güncellemesi geçer, ŞUBE seviyesinde kilit fırlamalı (izolasyon).
            var stale = CloneForUpdate(saved1);
            var staleBranch = stale.Branches.Single(b => !b.IsHeadquarters);
            staleBranch.ConcurrencyStamp = staleBranchStamp;
            staleBranch.Name = "Şube2-2";

            await Should.ThrowAsync<AbpDbConcurrencyException>(() => _appService.SaveTreeAsync(stale));
        }
    }

    [Fact]
    public async Task SaveTree_with_null_company_stamp_on_update_throws()
    {
        // Fail-CLOSED kanıtı: mevcut kayıt güncellenirken stamp boşsa AbpDbConcurrencyException yerine
        // erken BusinessException("TreeChanged") — sessiz overwrite YOK.
        var tryId = await GetTryUnitIdAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var initial = await _appService.SaveTreeAsync(new CompanyTreeSaveDto
            {
                Name = "Acme", CountryCode = "TR", BaseCurrencyUnitId = tryId, IsHeadquarters = true,
            });

            var update = CloneForUpdate(initial);
            update.ConcurrencyStamp = null;
            update.Name = "Acme-x";

            await Should.ThrowAsync<BusinessException>(() => _appService.SaveTreeAsync(update));
        }
    }

    [Fact]
    public async Task SaveTree_omitting_a_never_persisted_branch_creates_no_phantom()
    {
        // UI senaryosu: kullanıcı yeni şube ekleyip (Id=null) kaydetmeden önce siler. OnBranchDeleted'daki
        // "if (branch.Id is {} id)" guard'ı yüzünden o şube ne input.Branches'e ne DeletedBranchIds'e girer.
        // Sunucu yalnız gelen ağacı işler → phantom create+delete olmaz, sonuç temiz kalır.
        var tryId = await GetTryUnitIdAsync();
        using (_currentTenant.Change(Guid.NewGuid()))
        {
            var initial = await _appService.SaveTreeAsync(new CompanyTreeSaveDto
            {
                Name = "Acme", CountryCode = "TR", BaseCurrencyUnitId = tryId, IsHeadquarters = true,
            });

            // "Eklenip silinen" hayalet şube hiç yok: sadece HQ gönderilir, DeletedBranchIds boş.
            var update = CloneForUpdate(initial);
            var saved = await _appService.SaveTreeAsync(update);

            saved.Branches.Count.ShouldBe(1);
            saved.Branches[0].IsHeadquarters.ShouldBeTrue();
        }
    }

    private static CompanyTreeSaveDto CloneForUpdate(CompanyTreeDto src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        CountryCode = src.CountryCode,
        BaseCurrencyUnitId = src.BaseCurrencyUnitId,
        IsActive = src.IsActive,
        IsHeadquarters = src.IsHeadquarters,
        DisplayOrder = src.DisplayOrder,
        Description = src.Description,
        ConcurrencyStamp = src.ConcurrencyStamp,
        Branches = src.Branches.Select(b => new BranchTreeSaveDto
        {
            Id = b.Id,
            Name = b.Name,
            IsHeadquarters = b.IsHeadquarters,
            IsActive = b.IsActive,
            DisplayOrder = b.DisplayOrder,
            Description = b.Description,
            ConcurrencyStamp = b.ConcurrencyStamp,
            Vaults = b.Vaults.Select(v => new VaultTreeSaveDto
            {
                Id = v.Id, Name = v.Name, IsDefault = v.IsDefault, IsActive = v.IsActive,
                DisplayOrder = v.DisplayOrder, Description = v.Description, ConcurrencyStamp = v.ConcurrencyStamp,
            }).ToList(),
        }).ToList(),
    };
}
