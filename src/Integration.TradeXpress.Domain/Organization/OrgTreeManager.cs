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
    private readonly IDataFilter _dataFilter;

    // Company görünürlük filtresini KAPATMAK için IDataFilter (CompanyOwnedBackfiller ile aynı desen).
    // ICurrentCompany.Change YERİNE bu tercih edildi: ICurrentCompany'yi (ctor VEYA lazy) kullanmak DI DÖNGÜSÜ
    // yaratıyor — CurrentCompany → WorkingCompanyContextProvider → WorkingContextService → BranchAppService →
    // OrgTreeManager → (CurrentCompany). BranchAppService zaten OrgTreeManager'a bağımlı. IDataFilter bu zincire
    // bağımlı DEĞİL → döngü tamamen kırılır. Org kurulumu sistem işidir; company filtresi kapalıyken BranchId-özgü
    // kasa sorgusu doğru sonucu verir (working-context sentinel'i Guid.Empty'den etkilenmez, cross-company sızıntı
    // yok çünkü sorgu daima BranchId ile daraltılmış).
    public OrgTreeManager(
        IRepository<Branch, Guid> branchRepository,
        IRepository<Vault, Guid> vaultRepository,
        IDataFilter dataFilter)
    {
        _branchRepository = branchRepository;
        _vaultRepository = vaultRepository;
        _dataFilter = dataFilter;
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
            await EnsureBaseCurrencyInheritedAsync(hq, company);
            await EnsureDefaultVaultAsync(hq);
            return hq;
        }

        if (branches.Count > 0)
        {
            hq = branches.OrderBy(b => b.DisplayOrder).First();
            hq.SetAsHeadquarters(true);
            InheritBaseCurrencyIfMissing(hq, company);
            await _branchRepository.UpdateAsync(hq, autoSave: true);
            await EnsureDefaultVaultAsync(hq);
            return hq;
        }

        var branch = new Branch(
            company.Id,
            BranchConsts.DefaultHeadquartersCode,
            BranchConsts.DefaultHeadquartersName,
            isHeadquarters: true,
            displayOrder: 1);

        InheritBaseCurrencyIfMissing(branch, company);

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
        // Org kurulumu SİSTEM işidir → aktif kasa kontrolü kullanıcının working-context şirketine DEĞİL,
        // şubenin KENDİ şirketine göre yapılır. Aksi halde working-context boşken (yeni tenant admin'i henüz
        // şirket seçmemiş → izinli küme boş → Guid.Empty SENTINEL) ICompanyOwned görünürlük filtresi TÜM
        // kasaları gizler (permissive yalnız CurrentCompanyId=null iken); mevcut kasa görünmez → yeniden
        // "KASA" insert edilir → benzersizlik (TenantId,BranchId,Code) çakışması → BusinessException.
        // Disable<ICompanyScoped>: company görünürlük filtresini kapatır (anahtar tek: ICompanyScoped →
        // ICompanyOwned Vault da kapsanır). BranchId ile daraltılmış sorgu doğru kasayı bulur; DI döngüsü yok.
        using (_dataFilter.Disable<ICompanyScoped>())
        {
            var existing = await AsyncExecuter.ToListAsync(
                (await _vaultRepository.GetQueryableAsync()).Where(v => v.BranchId == branch.Id));
            existing = existing.OrderBy(v => v.DisplayOrder).ToList();

            var current = existing.FirstOrDefault(v => v.IsDefault);
            if (current != null)
            {
                return current;
            }

            if (existing.Count > 0)
            {
                var promote = existing.First();
                promote.SetAsDefault(true);
                await _vaultRepository.UpdateAsync(promote, autoSave: true);
                return promote;
            }

            var vault = new Vault(
                branch.CompanyId,
                branch.Id,
                VaultConsts.DefaultCode,
                VaultConsts.DefaultName,
                isDefault: true,
                displayOrder: 1);

            await _vaultRepository.InsertAsync(vault, autoSave: true);
            return vault;
        }
    }

    /// <summary>
    /// Şube-otoriter bilanço birimi değişmezi (bkz. .claude/rules/financials.md): şube kendi
    /// bilanço birimini TAŞIMALI. Otomatik kurulan/yükseltilen şubelerde birim boş kalmışsa
    /// şirketin base'ini devralır (varsayılan-devir); şirket base'i de boşsa (erken-seed) dokunmaz —
    /// değerleme zaten şirket-fallback ile çalışır, devir bir sonraki geçişte tamamlanır (idempotent).
    /// </summary>
    private static void InheritBaseCurrencyIfMissing(Branch branch, Company company)
    {
        if (branch.BaseCurrencyUnitId == Guid.Empty && company.BaseCurrencyUnitId != Guid.Empty)
        {
            branch.SetBaseCurrency(company.BaseCurrencyUnitId);
        }
    }

    /// <summary>Mevcut HQ şubede eksik bilanço birimini devralır ve kalıcılaştırır (backfill iyileştirmesi).</summary>
    private async Task EnsureBaseCurrencyInheritedAsync(Branch hq, Company company)
    {
        if (hq.BaseCurrencyUnitId == Guid.Empty && company.BaseCurrencyUnitId != Guid.Empty)
        {
            hq.SetBaseCurrency(company.BaseCurrencyUnitId);
            await _branchRepository.UpdateAsync(hq, autoSave: true);
        }
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
