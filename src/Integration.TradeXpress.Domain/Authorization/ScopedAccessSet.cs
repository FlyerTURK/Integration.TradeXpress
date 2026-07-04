namespace Integration.TradeXpress.Authorization;

/// <summary>
/// Bir kullanıcının çözümlenmiş kapsam (scope) erişim kümesi — <see cref="IScopedGrantResolver"/> üretir,
/// per-user cache'lenir. Coğrafi kapsam (Company/Branch/Vault) + Mode odaklıdır; RoleId / PermissionName
/// ayrımını UMURSAMAZ.
///
/// <para><b>Çözümleme kuralı — EN SPESİFİK KAPSAM KAZANIR:</b> tenant-geneli (özgüllük 0) &lt; şirket (1)
/// &lt; şube (2) &lt; kasa (3). Sorgulanan düğümün YOLU üzerindeki (ata + kendisi) uygulanabilir
/// kurallardan en spesifik olan(lar) kararı verir. Aynı en-spesifik seviyede Grant + Deny çakışırsa
/// <b>DENY KESİN ÜSTÜN</b> (fail-safe). Hiç uygulanabilir kural yoksa erişim YOK (varsayılan kapalı).</para>
///
/// <para><b>DİKKAT — "Deny üstün" yalnız AYNI seviyede geçerlidir:</b> "şirket Deny + daha spesifik şube
/// Grant" durumunda şube Grant daha spesifik olduğu için o şube <see cref="CanAccessBranch"/> ile
/// ERİŞİLİR — Deny yalnızca eşit özgüllükte kesin üstündür, daha spesifik bir Grant'i EZMEZ. Bunun sonucu:
/// <see cref="CanAccessCompany"/> KATI düğüm erişimidir (yalnız o şirket düğümünün yolundaki kurallar);
/// alt-ağaçtaki bir Grant nedeniyle şirketin combo'da "ulaşılabilir" sayılması ayrı bir kavramdır ve
/// <see cref="AllowedCompanyIds"/> (erişilebilirlik kümesi) ile ifade edilir.</para>
///
/// <para>Cache serileştirmesi: yalnız <see cref="Rules"/> (get/set) serileşir; erişim/erişilebilirlik
/// üyeleri <see cref="JsonIgnoreAttribute"/> ile türetilir (kuraldan hesaplanır, saklanmaz).</para>
/// </summary>
public class ScopedAccessSet
{
    /// <summary>Kullanıcının çözümlenmiş kapsam kuralları (cache'lenen tek durum).</summary>
    public List<ScopedGrantRule> Rules { get; set; } = new();

    public ScopedAccessSet()
    {
    }

    public ScopedAccessSet(List<ScopedGrantRule> rules)
    {
        Rules = rules ?? new List<ScopedGrantRule>();
    }

    /// <summary>Şirket düğümüne erişim (o şirketin YOLU üzerindeki kurallarda en-spesifik-kazanır).</summary>
    public bool CanAccessCompany(Guid companyId)
    {
        return IsGranted(companyId, null, null);
    }

    /// <summary>Şube düğümüne erişim (tenant → şirket → şube yolunda en-spesifik-kazanır).</summary>
    public bool CanAccessBranch(Guid companyId, Guid branchId)
    {
        return IsGranted(companyId, branchId, null);
    }

    /// <summary>Kasa düğümüne erişim (tenant → şirket → şube → kasa yolunda en-spesifik-kazanır).</summary>
    public bool CanAccessVault(Guid companyId, Guid branchId, Guid vaultId)
    {
        return IsGranted(companyId, branchId, vaultId);
    }

    /// <summary>
    /// Combo/görüntü daraltma için ULAŞILABİLİR şirket id'leri: net-Grant olan bir düğümü (kendisi ya da
    /// altındaki bir şube/kasa) bulunan, AÇIKÇA adı geçen şirketler. Tenant-geneli grant tek tek sayılamaz;
    /// o durum <see cref="IsTenantWide"/> ile ifade edilir (combo'yu bu kümeyle KATI daraltma).
    /// </summary>
    [JsonIgnore]
    public IReadOnlySet<Guid> AllowedCompanyIds
    {
        get
        {
            return BuildReachableCompanies();
        }
    }

    /// <summary>Combo/görüntü daraltma için ULAŞILABİLİR şube id'leri (net-Grant düğümü olan şubeler).</summary>
    [JsonIgnore]
    public IReadOnlySet<Guid> AllowedBranchIds
    {
        get
        {
            return BuildReachableBranches();
        }
    }

    /// <summary>
    /// Tenant-geneli (tüm şirketler) net Grant var mı? true ise kullanıcı <see cref="AllowedCompanyIds"/>
    /// dışındaki şirketlere de erişebilir → combo bu kümeyle KATI daraltılmamalı (yalnız açık Deny'ler
    /// <see cref="CanAccessCompany"/> ile ayıklanır).
    /// </summary>
    [JsonIgnore]
    public bool IsTenantWide
    {
        get
        {
            var hasGrant = false;
            foreach (var rule in Rules)
            {
                if (SpecificityOf(rule) != 0)
                {
                    continue;
                }

                // Aynı (tenant-geneli) seviyede Deny kesin üstün.
                if (rule.Mode == ScopedGrantMode.Deny)
                {
                    return false;
                }

                hasGrant = true;
            }

            return hasGrant;
        }
    }

    /// <summary>
    /// Verilen düğüme (company + opsiyonel branch + opsiyonel vault) erişim kararı. Uygulanabilir kurallar
    /// içinde en yüksek özgüllüğü bul; o seviyede Deny varsa red (fail-safe), yoksa Grant varsa kabul.
    /// </summary>
    private bool IsGranted(Guid companyId, Guid? branchId, Guid? vaultId)
    {
        // Sorgu düğümünün derinliği (kaç koordinat verildi).
        var queryDepth = 1 + (branchId.HasValue ? 1 : 0) + (vaultId.HasValue ? 1 : 0);

        var bestSpecificity = -1;
        var denyAtBest = false;
        var grantAtBest = false;

        foreach (var rule in Rules)
        {
            if (!AppliesTo(rule, companyId, branchId, vaultId, queryDepth))
            {
                continue;
            }

            var specificity = SpecificityOf(rule);
            if (specificity > bestSpecificity)
            {
                bestSpecificity = specificity;
                denyAtBest = rule.Mode == ScopedGrantMode.Deny;
                grantAtBest = rule.Mode == ScopedGrantMode.Grant;
            }
            else if (specificity == bestSpecificity)
            {
                if (rule.Mode == ScopedGrantMode.Deny)
                {
                    denyAtBest = true;
                }
                else
                {
                    grantAtBest = true;
                }
            }
        }

        if (bestSpecificity < 0)
        {
            // Uygulanabilir kural yok → varsayılan kapalı.
            return false;
        }

        if (denyAtBest)
        {
            // Aynı en-spesifik seviyede Deny kesin üstün.
            return false;
        }

        return grantAtBest;
    }

    /// <summary>
    /// Kural, sorgulanan düğümün YOLUNA uygulanabilir mi? Kural sorgudan daha derin olamaz (daha spesifik
    /// kapsam üst düğümü karara bağlamaz) ve kuralın dolu koordinatları sorgu yolundaki karşılıkla
    /// eşleşmelidir (null koordinat = "aşağıdaki her şey" → her zaman eşleşir).
    /// </summary>
    private static bool AppliesTo(ScopedGrantRule rule, Guid companyId, Guid? branchId, Guid? vaultId, int queryDepth)
    {
        if (SpecificityOf(rule) > queryDepth)
        {
            return false;
        }

        if (rule.CompanyId.HasValue && rule.CompanyId.Value != companyId)
        {
            return false;
        }

        if (rule.BranchId.HasValue && (!branchId.HasValue || rule.BranchId.Value != branchId.Value))
        {
            return false;
        }

        if (rule.VaultId.HasValue && (!vaultId.HasValue || rule.VaultId.Value != vaultId.Value))
        {
            return false;
        }

        return true;
    }

    /// <summary>Kuralın özgüllüğü = dolu koordinat sayısı (0 tenant-geneli … 3 kasa). Koordinatlar hiyerarşik
    /// (entity şube→şirket, kasa→şube gerektirir) olduğundan bu, kuralın derinliğine eşittir.</summary>
    private static int SpecificityOf(ScopedGrantRule rule)
    {
        var count = 0;
        if (rule.CompanyId.HasValue)
        {
            count++;
        }

        if (rule.BranchId.HasValue)
        {
            count++;
        }

        if (rule.VaultId.HasValue)
        {
            count++;
        }

        return count;
    }

    private HashSet<Guid> BuildReachableCompanies()
    {
        var result = new HashSet<Guid>();
        foreach (var rule in Rules)
        {
            if (!rule.CompanyId.HasValue)
            {
                // Tenant-geneli → tek tek sayılamaz (IsTenantWide ile ifade edilir).
                continue;
            }

            if (IsRuleNodeGranted(rule))
            {
                result.Add(rule.CompanyId.Value);
            }
        }

        return result;
    }

    private HashSet<Guid> BuildReachableBranches()
    {
        var result = new HashSet<Guid>();
        foreach (var rule in Rules)
        {
            if (!rule.BranchId.HasValue)
            {
                continue;
            }

            if (IsRuleNodeGranted(rule))
            {
                result.Add(rule.BranchId.Value);
            }
        }

        return result;
    }

    /// <summary>Kuralın KENDİ düğümü net Grant mı (en-spesifik-kazanır ile)? Ulaşılabilirlik kümeleri için.</summary>
    private bool IsRuleNodeGranted(ScopedGrantRule rule)
    {
        return IsGranted(rule.CompanyId!.Value, rule.BranchId, rule.VaultId);
    }
}
