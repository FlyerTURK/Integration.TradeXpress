using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.EventBus;
using Volo.Abp.Identity;

namespace Integration.TradeXpress.Authorization;

/// <summary>
/// Yeni kullanıcı oluşturulunca (LOCAL entity-created event) ona tenant-geneli grant garanti eder →
/// resolution-time doğrulama devreye girince yeni kullanıcı ilk girişte KİLİTLENMEZ.
///
/// <para><b>Neden LOCAL event:</b> ABP 10.4.1 Identity, distributed <c>IdentityUserCreatedEto</c>
/// YAYINLAMAZ (pakette böyle bir ETO yok) → <see cref="ILocalEventHandler{T}"/> +
/// <see cref="EntityCreatedEventData{IdentityUser}"/> kullanılır (Blazor server in-process güvenilir; aynı
/// UoW'de çalışır).</para>
///
/// <para>Mantık <see cref="ScopedGrantSeeder.EnsureTenantWideGrantsAsync"/>'e delege (DRY; idempotent). Yeni
/// kullanıcı genelde henüz rolsüz oluşturulur → coğrafi-only grant; sonradan rol atanırsa batch seed eksik
/// rol-grant'larını tamamlar. Yalnız TENANT kullanıcıları için çalışır (host'un org ağacı/working-context'i
/// yok → grant anlamsız).</para>
/// </summary>
public class ScopedGrantUserCreatedHandler
    : ILocalEventHandler<EntityCreatedEventData<IdentityUser>>, ITransientDependency
{
    private readonly ScopedGrantSeeder _seeder;
    private readonly ICurrentTenant _currentTenant;

    public ScopedGrantUserCreatedHandler(ScopedGrantSeeder seeder, ICurrentTenant currentTenant)
    {
        _seeder = seeder;
        _currentTenant = currentTenant;
    }

    public async Task HandleEventAsync(EntityCreatedEventData<IdentityUser> eventData)
    {
        var user = eventData.Entity;
        if (!user.TenantId.HasValue)
        {
            return; // host kullanıcısı → working-context yok, grant gereksiz
        }

        // Grant kullanıcının tenant'ında yazılmalı (IMultiTenant TenantId'yi CurrentTenant'tan alır).
        using (_currentTenant.Change(user.TenantId))
        {
            await _seeder.EnsureTenantWideGrantsAsync(user.Id);
        }
    }
}
