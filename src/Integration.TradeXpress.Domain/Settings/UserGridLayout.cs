namespace Integration.TradeXpress.Settings;

/// <summary>
/// Kullanıcı başına grid kolon düzeni (genişlik/sıra/sıralama vb.) kalıcılığı. Her grid bir satır
/// (<see cref="GridKey"/>), <see cref="Layout"/> = DevExpress GridPersistentLayout JSON (nvarchar(max)).
/// Önceki "tüm grid'ler tek ayar sözlüğü" yaklaşımı AbpSettings.Value'yu TRUNCATE ediyordu → ayrı tablo.
/// Per-tenant (IMultiTenant).
/// </summary>
public class UserGridLayout : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip kullanıcı (IdentityUser, id-only).</summary>
    public virtual Guid UserId { get; protected set; }

    /// <summary>Grid kimliği (ör. "Şirketler:v3", "Drill:Branch:v1").</summary>
    public virtual string GridKey { get; protected set; } = null!;

    /// <summary>DevExpress GridPersistentLayout serileştirilmiş JSON (nvarchar(max)).</summary>
    public virtual string Layout { get; protected set; } = null!;

    protected UserGridLayout() { }

    public UserGridLayout(Guid userId, string gridKey, string layout)
    {
        UserId = userId;
        GridKey = gridKey;
        Layout = layout;
    }

    public virtual void SetLayout(string layout)
    {
        Layout = layout;
    }
}
