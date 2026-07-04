namespace Integration.TradeXpress.Authorization;

/// <summary>
/// Bir kullanıcının kapsam (scope) grant'larını <see cref="ScopedAccessSet"/>'e çözümler ve per-user
/// cache'ler. Coğrafi kapsam (Company/Branch/Vault) + Grant/Deny odaklıdır. Faz 4 working-context
/// yetkilendirmesinin çekirdeği — SAF EKLEME (henüz kimse çağırmaz; sonraki adımlar kullanır).
/// </summary>
public interface IScopedGrantResolver
{
    /// <summary>Kullanıcının çözümlenmiş erişim kümesini döner (cache'li; ilk çağrıda DB'den kurulur).</summary>
    Task<ScopedAccessSet> ResolveAsync(Guid userId);

    /// <summary>Kullanıcının cache'ini geçersiz kılar (grant ekleme/silme sonrası çağrılmalı).</summary>
    Task InvalidateAsync(Guid userId);
}
