using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Bir entity kaydını (<see cref="EntityName"/> + <see cref="EntityId"/>, ör. "Good"/"GoodVariant") merkezi kütüphanedeki
/// bir <see cref="Media"/>'ya bağlayan REFERANS (id-only; aggregate'ler arası nav yok). Aynı medya çok kayda linklenebilir
/// (reuse). Sıra (<see cref="DisplayOrder"/>), varsayılan (<see cref="IsDefault"/>) ve aktif/pasif (<see cref="IsActive"/>)
/// PER-LINK'tir — aynı medya bir kayıtta varsayılan, başka kayıtta pasif olabilir. Kayıt-başı link seti replace-all
/// (EntityMediaAppService.ReplaceForAsync) ile yönetilir; medya içeriği silinmez (kütüphanede kalır).
/// </summary>
public class EntityMediaLink : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected EntityMediaLink()
    {
    }

    public EntityMediaLink(
        Guid? companyId,
        string entityName,
        Guid entityId,
        Guid mediaId,
        int displayOrder,
        bool isDefault,
        bool isActive)
    {
        CompanyId = companyId;
        SetOwner(entityName, entityId);
        SetMedia(mediaId);
        DisplayOrder = displayOrder;
        IsDefault = isDefault;
        IsActive = isActive;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Sahip entity tipi adı (ör. "Good", "GoodVariant") — set-once.</summary>
    public virtual string EntityName { get; protected set; } = null!;

    /// <summary>Sahip kayıt Id'si — set-once. Varyant-özel medya AYRI bir <see cref="EntityName"/> ile taşınır
    /// ("GoodVariant"/"MetalVariant"/"ProductVariant" + varyantın Id'si); link üzerinde varyant kolonu YOKTUR.</summary>
    public virtual Guid EntityId { get; protected set; }

    /// <summary>Kütüphanedeki medya (id-only referans; aggregate nav yok).</summary>
    public virtual Guid MediaId { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }

    /// <summary>Bu kayıt için varsayılan (ana) medya — tekil garanti AppService normalize'ında. Varsayılan pasif olamaz.</summary>
    public virtual bool IsDefault { get; protected set; }

    /// <summary>Bu kayıt için aktif mi — pasif medya dış yüzeylerde (pazaryeri push vb.) atlanabilir.</summary>
    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetDisplayOrder(int value)
    {
        DisplayOrder = value;
    }

    public virtual void SetAsDefault(bool value)
    {
        IsDefault = value;
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public override string ToString()
    {
        return $"{EntityName}:{EntityId}->{MediaId}";
    }

    private void SetOwner(string entityName, Guid entityId)
    {
        EntityName = StringFieldGuard.EnsureRequiredText(entityName, nameof(EntityName), 1, MediaConsts.EntityNameMaxLength);
        if (entityId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(EntityId));
        }

        EntityId = entityId;
    }

    private void SetMedia(Guid mediaId)
    {
        if (mediaId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(MediaId));
        }

        MediaId = mediaId;
    }

    #endregion
}
