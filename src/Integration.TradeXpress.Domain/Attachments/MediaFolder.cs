using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Medya kütüphanesi KLASÖRÜ — company-scoped, hiyerarşik (<see cref="ParentId"/> ile ağaç). Yalnız organizasyon içindir;
/// içerik TAŞIMAZ — medya klasöre <see cref="Media.FolderId"/> ile REFERANS verir. Klasör silinince içindeki medya
/// SİLİNMEZ (üst klasöre taşınır); alt klasörler de üste taşınır → ağaç kopmaz.
/// </summary>
public class MediaFolder : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected MediaFolder()
    {
    }

    public MediaFolder(Guid? companyId, string name, Guid? parentId)
    {
        CompanyId = companyId;
        SetName(name);
        ParentId = parentId;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket (company-scope; null = tenant-geneli).</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Üst klasör (null = kök). Hiyerarşi bağı.</summary>
    public virtual Guid? ParentId { get; protected set; }

    public virtual string Name { get; protected set; } = null!;

    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, MediaConsts.FolderNameMaxLength);
    }

    public virtual void SetParent(Guid? parentId)
    {
        ParentId = parentId;
    }

    public virtual void SetDisplayOrder(int displayOrder)
    {
        DisplayOrder = displayOrder;
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}
