namespace Integration.TradeXpress.Authorization;

/// <summary>
/// Çözümlenmiş TEK bir kapsam kuralı — yalnız coğrafi koordinatlar (Company/Branch/Vault) + <see cref="Mode"/>.
/// RoleId / PermissionName TAŞINMAZ: çözümleme yalnız coğrafi kapsam + Grant/Deny'i umursar (rol/izin ayrımı
/// ileriki permission-provider adımının işi). Cache serileştirmesi için düz get/set (parametresiz ctor).
/// null koordinat = "aşağıdaki her şey" (ör. CompanyId dolu + BranchId null = o şirketin tüm şubeleri).
/// </summary>
public class ScopedGrantRule
{
    /// <summary>Şirket kapsamı (null = tenant geneli / her şirket).</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>Şube kapsamı (null = şirketteki tüm şubeler).</summary>
    public Guid? BranchId { get; set; }

    /// <summary>Kasa kapsamı (null = şubedeki tüm kasalar).</summary>
    public Guid? VaultId { get; set; }

    /// <summary>Grant (izin ver) ya da Deny (kısıtla).</summary>
    public ScopedGrantMode Mode { get; set; }
}
