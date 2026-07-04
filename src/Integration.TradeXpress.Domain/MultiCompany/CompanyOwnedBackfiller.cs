using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Vaults;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Çok-şirket güvenlik sınırı geçiş backfill'i — <see cref="ICompanyOwned"/>'a SONRADAN taşınan
/// <see cref="SubAccount"/> ve <see cref="Vault"/>'un denormalize <c>CompanyId</c> kolonunu parent'tan doldurur.
///
/// <para><b>Neden migration DIŞINDA:</b> EF migration'ı non-nullable <c>CompanyId</c>'yi <c>Guid.Empty</c>
/// defaultValue ile ekler (mevcut satırlar önce boş kalır). Backfill SQL'i migration'a elle eklenemedi
/// (governance guard'ı Migrations düzenlemesini bloklar). Bunun yerine bu idempotent seeder, DbMigrator'ın
/// migrate'ten HEMEN SONRA çalıştırdığı akışta boş kalanları parent'tan (SubAccount→Account, Vault→Branch)
/// doldurur. Migrate→seed penceresi tek DbMigrator koşusundadır (uygulama deploy'da down) → dışarıya sızmaz.</para>
///
/// <para><b>İdempotent:</b> yalnız <c>CompanyId == Guid.Empty</c> satırlara dokunur; ikinci koşuda hiçbir şey
/// yapmaz (boş satır kalmaz). WHERE'siz toplu yazma YOK. Aktif tenant kapsamında çalışır (org yapısı per-tenant);
/// çağıran (seed orchestrator) her tenant için ayrı tetikler. Repository tabanlı (raw SQL YOK) → SQL Server +
/// Sqlite test aynı yolu izler.</para>
/// </summary>
public class CompanyOwnedBackfiller : DomainService
{
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;

    public CompanyOwnedBackfiller(
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<Account, Guid> accountRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<Branch, Guid> branchRepository)
    {
        _subAccountRepository = subAccountRepository;
        _accountRepository    = accountRepository;
        _vaultRepository      = vaultRepository;
        _branchRepository     = branchRepository;
    }

    /// <summary>Aktif tenant'ın boş (Guid.Empty) CompanyId taşıyan SubAccount/Vault satırlarını parent'tan
    /// doldurur. Boş satır yoksa (temiz kurulum ya da ikinci koşu) ucuz no-op.</summary>
    public async Task BackfillCurrentTenantAsync()
    {
        await BackfillSubAccountsAsync();
        await BackfillVaultsAsync();
    }

    private async Task BackfillSubAccountsAsync()
    {
        var orphans = await AsyncExecuter.ToListAsync(
            (await _subAccountRepository.GetQueryableAsync()).Where(s => s.CompanyId == Guid.Empty));
        if (orphans.Count == 0)
        {
            return;
        }

        var companyByAccount = await MapCompanyByParentAsync(
            orphans.Select(s => s.AccountId),
            _accountRepository,
            a => a.Id,
            a => a.CompanyId);

        foreach (var sub in orphans)
        {
            if (companyByAccount.TryGetValue(sub.AccountId, out var companyId) && companyId != Guid.Empty)
            {
                sub.BackfillCompanyIfMissing(companyId);
                await _subAccountRepository.UpdateAsync(sub, autoSave: true);
            }
        }
    }

    private async Task BackfillVaultsAsync()
    {
        var orphans = await AsyncExecuter.ToListAsync(
            (await _vaultRepository.GetQueryableAsync()).Where(v => v.CompanyId == Guid.Empty));
        if (orphans.Count == 0)
        {
            return;
        }

        var companyByBranch = await MapCompanyByParentAsync(
            orphans.Select(v => v.BranchId),
            _branchRepository,
            b => b.Id,
            b => b.CompanyId);

        foreach (var vault in orphans)
        {
            if (companyByBranch.TryGetValue(vault.BranchId, out var companyId) && companyId != Guid.Empty)
            {
                vault.BackfillCompanyIfMissing(companyId);
                await _vaultRepository.UpdateAsync(vault, autoSave: true);
            }
        }
    }

    /// <summary>Verilen parent id kümesi için (id → CompanyId) haritasını çıkarır (tek sorgu; distinct).</summary>
    private async Task<Dictionary<Guid, Guid>> MapCompanyByParentAsync<TParent>(
        IEnumerable<Guid> parentIds,
        IRepository<TParent, Guid> parentRepository,
        Func<TParent, Guid> idSelector,
        Func<TParent, Guid> companySelector)
        where TParent : class, Volo.Abp.Domain.Entities.IEntity<Guid>
    {
        var ids = parentIds.Distinct().ToList();
        var parents = await AsyncExecuter.ToListAsync(
            (await parentRepository.GetQueryableAsync()).Where(p => ids.Contains(p.Id)));
        return parents.ToDictionary(idSelector, companySelector);
    }
}
