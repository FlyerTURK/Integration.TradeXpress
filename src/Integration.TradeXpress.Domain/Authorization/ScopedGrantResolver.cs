namespace Integration.TradeXpress.Authorization;

/// <summary>
/// <see cref="IScopedGrantResolver"/> uygulaması. Kullanıcının tüm kapsam grant'larını TEK sorguda
/// (index'li: <c>IX_AppUserScopedGrants_TenantId_UserId</c>) çeker, coğrafi koordinat + Mode'a indirger
/// ve per-user cache'ler. Çözümleme mantığı <see cref="ScopedAccessSet"/> içindedir (en-spesifik-kazanır,
/// eşit seviyede Deny üstün). Cache anahtarı TenantId + UserId (tenant-agnostik cache deposu için).
/// </summary>
public class ScopedGrantResolver : DomainService, IScopedGrantResolver
{
    private readonly IRepository<UserScopedGrant, Guid> _repository;
    private readonly IDistributedCache<ScopedAccessSet> _cache;

    public ScopedGrantResolver(
        IRepository<UserScopedGrant, Guid> repository,
        IDistributedCache<ScopedAccessSet> cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public virtual async Task<ScopedAccessSet> ResolveAsync(Guid userId)
    {
        var key = BuildCacheKey(userId);
        var result = await _cache.GetOrAddAsync(key, () => BuildFromStoreAsync(userId));
        return result ?? new ScopedAccessSet();
    }

    public virtual async Task InvalidateAsync(Guid userId)
    {
        await _cache.RemoveAsync(BuildCacheKey(userId));
    }

    private async Task<ScopedAccessSet> BuildFromStoreAsync(Guid userId)
    {
        // Tek sorgu: kullanıcının bu tenant'taki tüm grant'ları (index'li). TenantId filtresi ABP data
        // filter'a ek olarak açık (AppService deseniyle hizalı).
        var query = (await _repository.GetQueryableAsync())
            .Where(g => g.UserId == userId && g.TenantId == CurrentTenant.Id);
        var grants = await AsyncExecuter.ToListAsync(query);

        var rules = grants
            .Select(g => new ScopedGrantRule
            {
                CompanyId = g.CompanyId,
                BranchId = g.BranchId,
                VaultId = g.VaultId,
                Mode = g.Mode,
            })
            .ToList();

        return new ScopedAccessSet(rules);
    }

    private string BuildCacheKey(Guid userId)
    {
        // Per-tenant + per-user. Tenant boyutu ANAHTARDA taşınır (host = tenant'sız).
        var tenantPart = CurrentTenant.Id?.ToString() ?? "host";
        return $"{tenantPart}:{userId}";
    }
}
