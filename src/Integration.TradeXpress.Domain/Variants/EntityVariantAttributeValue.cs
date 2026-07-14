using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// Varyant ↔ nitelik-değer bağı — bir <see cref="EntityVariant"/>'ın bir nitelik için SEÇİLİ değeri. Varyantın
/// kombinasyon kimliğini kurar: her nitelikten bir satır ({Renk:Kırmızı},{Beden:42}…). <see cref="EntityAttributeId"/>
/// denormalize tutulur → "varyant başına nitelik başına TEK değer" değişmezi tek unique index'le
/// (VariantId, AttributeId) zorlanır. Company-scoped (denormalize) + per-tenant. Tümü set-once.
/// </summary>
public class EntityVariantAttributeValue : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected EntityVariantAttributeValue()
    {
    }

    public EntityVariantAttributeValue(
        Guid? companyId,
        Guid entityVariantId,
        Guid entityAttributeId,
        Guid entityAttributeValueId)
    {
        CompanyId = companyId;
        EntityVariantId = Require(entityVariantId, nameof(EntityVariantId));
        EntityAttributeId = Require(entityAttributeId, nameof(EntityAttributeId));
        EntityAttributeValueId = Require(entityAttributeValueId, nameof(EntityAttributeValueId));
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — denormalize (null = tenant-geneli). Değişmez.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Sahip varyant — id-only referans. Değişmez.</summary>
    public virtual Guid EntityVariantId { get; protected set; }

    /// <summary>Nitelik — id-only (denormalize, "nitelik başına tek değer" unique index'i için). Değişmez.</summary>
    public virtual Guid EntityAttributeId { get; protected set; }

    /// <summary>Seçili nitelik değeri — id-only referans. Değişmez.</summary>
    public virtual Guid EntityAttributeValueId { get; protected set; }

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"{EntityVariantId}:{EntityAttributeValueId}";
    }

    private static Guid Require(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new RequiredPropertyException(name);
        }

        return id;
    }

    #endregion
}
