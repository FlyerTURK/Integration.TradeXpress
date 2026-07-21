using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Integration.TradeXpress.EtsyTaxonomies;

/// <summary>
/// Etsy seller taxonomy düğümü — <b>HOST-GLOBAL</b> referans taksonomi. <see cref="IMultiTenant"/> DEĞİL (TenantId
/// YOK) → tüm tenant'lar aynı ağacı paylaşır; host bir kez REST <c>/application/seller-taxonomy/nodes</c>'ten
/// sync'ler (N11Category ikizi). Ürünler yalnız <see cref="IsLeaf"/> (last-level) düğüme tanımlanır. Node kimliği
/// olan Etsy long id'si string'e çevrilir (matematik değil, kimlik).
/// </summary>
public class EtsyTaxonomy : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected EtsyTaxonomy()
    {
    }

    public EtsyTaxonomy(string externalId, string? parentExternalId, string name, bool isLeaf, int level)
    {
        // ExternalId set-once (Etsy'den gelir): normalize YOK, yalnız null/uzunluk guard'ı (kimlik, matematik değil).
        ExternalId = StringFieldGuard.EnsureRequiredText(externalId, nameof(ExternalId), 1, EtsyTaxonomyConsts.ExternalIdMaxLength);
        SetParent(parentExternalId);
        SetName(name);
        IsLeaf = isLeaf;
        Level = level;
    }

    #endregion

    #region Properties

    /// <summary>Etsy taxonomy node id'si (numerik ama matematik değil → string). Global benzersiz.</summary>
    public string ExternalId { get; protected set; } = string.Empty;

    /// <summary>Üst düğüm Etsy id'si (kök seviyede null). Ağaç REST <c>parent_id</c>'sinden kurulur.</summary>
    public string? ParentExternalId { get; protected set; }

    public string Name { get; protected set; } = string.Empty;

    /// <summary>Dip seviye (last-level) mi — ürün yalnız buraya tanımlanır. Etsy <c>children</c> boşsa yaprak.</summary>
    public bool IsLeaf { get; protected set; }

    /// <summary>Etsy node derinlik seviyesi (<c>level</c>) — 1'den başlar (kök). Breadcrumb/sıralama için taşınır.</summary>
    public int Level { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, EtsyTaxonomyConsts.NameMaxLength);
    }

    public virtual void SetParent(string? parentExternalId)
    {
        ParentExternalId = string.IsNullOrWhiteSpace(parentExternalId) ? null : parentExternalId.Trim();
    }

    public virtual void SetIsLeaf(bool isLeaf)
    {
        IsLeaf = isLeaf;
    }

    public virtual void SetLevel(int level)
    {
        Level = level;
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}
