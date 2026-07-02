using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Authorization;

/// <summary>
/// Bir kullanıcıya (IdentityUser, id-only referans) kapsamlı (scoped) ROL ya da doğrudan İZİN ataması.
/// Kapsam koordinatları nullable: hepsi null = tenant geneli; CompanyId dolu = şirket; +BranchId = şube;
/// +VaultId = kasa (null = "aşağıdaki her şey"). Tam olarak BİRİ dolu: <see cref="RoleId"/> ya da
/// <see cref="PermissionName"/>. <see cref="Mode"/> Grant/Deny — çözümlemede en spesifik kapsam kazanır.
/// Per-tenant (IMultiTenant). Çözümleme/working-context Faz 2'de (bu entity yalnız atamayı saklar).
/// </summary>
public class UserScopedGrant : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Atanan kullanıcı — IdentityUser'a id-only referans (nav YOK).</summary>
    public virtual Guid UserId { get; protected set; }

    /// <summary>Rol kapsamı (IdentityRole.Id). PermissionName ile XOR.</summary>
    public virtual Guid? RoleId { get; protected set; }

    /// <summary>Doğrudan izin kapsamı (permission adı). RoleId ile XOR.</summary>
    public virtual string? PermissionName { get; protected set; }

    /// <summary>Kapsam: şirket (null = tenant geneli / aşağıdaki her şirket).</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Kapsam: şube (null = şirketteki tüm şubeler). Doluysa CompanyId zorunlu.</summary>
    public virtual Guid? BranchId { get; protected set; }

    /// <summary>Kapsam: kasa (null = şubedeki tüm kasalar). Doluysa BranchId zorunlu.</summary>
    public virtual Guid? VaultId { get; protected set; }

    /// <summary>Grant (izin ver) ya da Deny (kısıtla). En spesifik kapsam kazanır (Faz 2 çözümleme).</summary>
    public virtual ScopedGrantMode Mode { get; protected set; }

    protected UserScopedGrant() { }

    public UserScopedGrant(
        Guid userId,
        Guid? roleId,
        string? permissionName,
        Guid? companyId,
        Guid? branchId,
        Guid? vaultId,
        ScopedGrantMode mode = ScopedGrantMode.Grant)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(userId));

        var hasRole = roleId.HasValue && roleId.Value != Guid.Empty;
        var hasPermission = !string.IsNullOrWhiteSpace(permissionName);
        if (hasRole == hasPermission)
            throw new BusinessException("TradeXpress:ScopedGrant:RoleXorPermission")
                .WithData("detail", "Tam olarak biri (RoleId ya da PermissionName) verilmeli.");

        // Kapsam hiyerarşisi tutarlılığı: alt seviye üst seviyeyi gerektirir.
        if (branchId.HasValue && !companyId.HasValue)
            throw new BusinessException("TradeXpress:ScopedGrant:BranchRequiresCompany");
        if (vaultId.HasValue && !branchId.HasValue)
            throw new BusinessException("TradeXpress:ScopedGrant:VaultRequiresBranch");

        UserId = userId;
        RoleId = hasRole ? roleId : null;
        PermissionName = hasPermission ? permissionName!.Trim() : null;
        CompanyId = companyId;
        BranchId = branchId;
        VaultId = vaultId;
        Mode = mode;
    }

    public virtual void SetMode(ScopedGrantMode mode) => Mode = mode;
}
