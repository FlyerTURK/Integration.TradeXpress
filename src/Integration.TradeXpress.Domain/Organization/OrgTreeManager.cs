namespace Integration.TradeXpress.Organization;

/// <summary>
/// Şirket → Şube → Kasa ağacının otomatik kurulum ve cascade-silme mantığını tek noktada toplar —
/// böylece UI (AppService) ve seed yolları aynı değişmezleri (invariant) paylaşır:
/// <list type="bullet">
/// <item>Her şirket en az bir <b>merkez (HQ) şube</b> ile yaşar (yoksa kurulur — idempotent).</item>
/// <item>Her şube en az bir <b>varsayılan kasa</b> ile yaşar.</item>
/// <item>Şirket/şube silinince çocukları (şube→kasa) cascade silinir.</item>
/// </list>
/// Çağıran taraf tenant scope'unu (CurrentTenant) ayarlar; burada CompanyId/BranchId ile sorgulanır.
/// </summary>
public class OrgTreeManager : DomainService
{
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;

    public OrgTreeManager(
        IRepository<Branch, Guid> branchRepository,
        IRepository<Vault, Guid> vaultRepository)
    {
        _branchRepository = branchRepository;
        _vaultRepository = vaultRepository;
    }

    /// <summary>
    /// Şirketin merkez (HQ) şubesini garanti eder (idempotent). HQ varsa onu döner; HQ yok ama şube
    /// varsa ilkini HQ yapar; hiç şube yoksa varsayılan "Merkez Şube"yi kurar ve ona bir varsayılan
    /// kasa açar. Mevcut şirketlerin backfill'i de buradan geçer.
    /// </summary>
    public async Task<Branch> EnsureHeadquartersBranchAsync(Company company)
    {
        var branches = await AsyncExecuter.ToListAsync(
            (await _branchRepository.GetQueryableAsync()).Where(b => b.CompanyId == company.Id));

        var hq = branches.FirstOrDefault(b => b.IsHeadquarters);
        if (hq != null)
        {
            await EnsureDefaultVaultAsync(hq);
            return hq;
        }

        if (branches.Count > 0)
        {
            hq = branches.OrderBy(b => b.DisplayOrder).First();
            hq.SetAsHeadquarters(true);
            await _branchRepository.UpdateAsync(hq, autoSave: true);
            await EnsureDefaultVaultAsync(hq);
            return hq;
        }

        var branch = new Branch(
            company.Id,
            BranchConsts.DefaultHeadquartersCode,
            BranchConsts.DefaultHeadquartersName,
            isHeadquarters: true,
            displayOrder: 1,
            tenantId: company.TenantId);

        await _branchRepository.InsertAsync(branch, autoSave: true);
        await EnsureDefaultVaultAsync(branch);
        return branch;
    }

    /// <summary>
    /// Şubenin varsayılan kasasını garanti eder (idempotent). Hiç kasa yoksa varsayılan "Ana Kasa"yı
    /// kurar; kasa var ama hiçbiri varsayılan değilse en düşük sıralı kasayı varsayılana yükseltir
    /// (tek-varsayılan invariant'ı — HQ şube mantığıyla simetrik).
    /// </summary>
    public async Task<Vault> EnsureDefaultVaultAsync(Branch branch)
    {
        var existing = await AsyncExecuter.ToListAsync(
            (await _vaultRepository.GetQueryableAsync()).Where(v => v.BranchId == branch.Id));
        existing = existing.OrderBy(v => v.DisplayOrder).ToList();

        var current = existing.FirstOrDefault(v => v.IsDefault);
        if (current != null)
            return current;

        if (existing.Count > 0)
        {
            var promote = existing.First();
            promote.SetAsDefault(true);
            await _vaultRepository.UpdateAsync(promote, autoSave: true);
            return promote;
        }

        var vault = new Vault(
            branch.Id,
            VaultConsts.DefaultCode,
            VaultConsts.DefaultName,
            isDefault: true,
            displayOrder: 1,
            tenantId: branch.TenantId);

        await _vaultRepository.InsertAsync(vault, autoSave: true);
        return vault;
    }

    /// <summary>Şubenin tüm kasalarını siler (şube silinmeden önce çağrılır).</summary>
    public async Task DeleteVaultsOfBranchAsync(Guid branchId, bool autoSave = true)
    {
        await _vaultRepository.DeleteAsync(v => v.BranchId == branchId, autoSave: autoSave);
    }

    /// <summary>Şirketin tüm şubelerini ve onların kasalarını siler (şirket silinmeden önce çağrılır).</summary>
    public async Task DeleteBranchesOfCompanyAsync(Guid companyId, bool autoSave = true)
    {
        var branchIds = await AsyncExecuter.ToListAsync(
            (await _branchRepository.GetQueryableAsync()).Where(b => b.CompanyId == companyId).Select(b => b.Id));

        foreach (var branchId in branchIds)
            await DeleteVaultsOfBranchAsync(branchId, autoSave);

        await _branchRepository.DeleteAsync(b => b.CompanyId == companyId, autoSave: autoSave);
    }
}
