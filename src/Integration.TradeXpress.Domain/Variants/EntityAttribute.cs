using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// Agnostik nitelik (varyant ekseni) — herhangi bir entity'ye <see cref="EntityName"/> + <see cref="EntityId"/> ile
/// bağlı (set-once). Ör. Good "Renk"/"Beden". Sahip entity başına en fazla
/// <see cref="EntityVariantConsts.MaxAttributesPerEntity"/>. Değerleri <c>EntityAttributeValue</c>'lardır; varyantlar
/// değer KOMBİNASYONLARINDAN doğar (senkron Domain'de). Company-scoped (sahip entity'den denormalize) + per-tenant.
/// SpecialCode/EntityImage agnostik deseniyle hizalı — TEK tablo tüm entity'lere hizmet eder.
/// </summary>
public class EntityAttribute : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected EntityAttribute()
    {
    }

    public EntityAttribute(Guid? companyId, string entityName, Guid entityId, string name, int displayOrder = 0)
    {
        CompanyId = companyId;
        SetOwner(entityName, entityId);
        SetName(name);
        DisplayOrder = displayOrder;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — sahip entity'den denormalize (null = tenant-geneli). Değişmez.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Sahip entity tipi adı (ör. "Good") — set-once.</summary>
    public virtual string EntityName { get; protected set; } = null!;

    /// <summary>Sahip entity Id'si — set-once.</summary>
    public virtual Guid EntityId { get; protected set; }

    public virtual string Name { get; protected set; } = null!;

    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, EntityVariantConsts.AttributeNameMaxLength);
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    public override string ToString()
    {
        return Name;
    }

    private void SetOwner(string entityName, Guid entityId)
    {
        EntityName = StringFieldGuard.EnsureRequiredText(
            entityName, nameof(EntityName), 1, EntityVariantConsts.EntityNameMaxLength);
        if (entityId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(EntityId));
        }

        EntityId = entityId;
    }

    #endregion
}
