using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// Agnostik nitelik değeri — bir <see cref="EntityAttribute"/>'a bağlı (<see cref="EntityAttributeId"/> set-once).
/// Ör. Renk → "Kırmızı", Beden → "42"/"XL". Varyantlar her nitelikten bir değer seçilerek oluşan kombinasyonlardır
/// (<c>EntityVariantAttributeValue</c> bağı). Company-scoped (denormalize) + per-tenant.
/// <para>Değer CASE-KORUR (EnsureRequiredText, min 1): perakende bedenleri "XL"/"M" bozulmasın.</para>
/// </summary>
public class EntityAttributeValue : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected EntityAttributeValue()
    {
    }

    public EntityAttributeValue(Guid? companyId, Guid entityAttributeId, string value, int displayOrder = 0)
    {
        CompanyId = companyId;
        SetAttribute(entityAttributeId);
        SetValue(value);
        DisplayOrder = displayOrder;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — denormalize (null = tenant-geneli). Değişmez.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Sahip nitelik — id-only, set-once.</summary>
    public virtual Guid EntityAttributeId { get; protected set; }

    public virtual string Value { get; protected set; } = null!;

    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Değer — CASE-KORUR (trim + min 1 + max); "XL"/"42"/"M" olduğu gibi saklanır (TitleCase YOK).</summary>
    public virtual void SetValue(string value)
    {
        Value = StringFieldGuard.EnsureRequiredText(
            value, nameof(Value), 1, EntityVariantConsts.AttributeValueMaxLength);
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    public override string ToString()
    {
        return Value;
    }

    private void SetAttribute(Guid entityAttributeId)
    {
        if (entityAttributeId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(EntityAttributeId));
        }

        EntityAttributeId = entityAttributeId;
    }

    #endregion
}
