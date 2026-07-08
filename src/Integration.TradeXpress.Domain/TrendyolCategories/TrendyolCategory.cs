namespace Integration.TradeXpress.TrendyolCategories;

/// <summary>
/// Trendyol kategori ağacı düğümü — <b>HOST-GLOBAL</b> referans taksonomi. <see cref="IMultiTenant"/> DEĞİL (TenantId
/// YOK) → tüm tenant'lar aynı ağacı paylaşır; host bir kez REST <c>/integration/product/product-categories</c>'ten
/// sync'ler (<see cref="N11Categories.N11Category"/> ikizi). Attribute/value SAKLANMAZ — bir yaprak kategori seçilince
/// ilgili SalesChannel'ın KENDİ kimliğiyle on-demand çekilir (T2). Ürünler yalnız <see cref="IsLeaf"/> (subCategories
/// boş) düğüme tanımlanır. N11'in komisyon/valör alanları Trendyol'da yok (ayrı kaynak) → o blok düşer.
/// </summary>
public class TrendyolCategory : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected TrendyolCategory()
    {
    }

    public TrendyolCategory(string externalId, string? parentExternalId, string name, bool isLeaf)
    {
        // ExternalId set-once (Trendyol'dan gelir): normalize YOK, yalnız null/uzunluk guard'ı (kimlik, matematik değil).
        ExternalId = StringFieldGuard.EnsureRequiredText(externalId, nameof(ExternalId), 1, TrendyolCategoryConsts.ExternalIdMaxLength);
        SetParent(parentExternalId);
        SetName(name);
        IsLeaf = isLeaf;
    }

    #endregion

    #region Properties

    /// <summary>Trendyol kategori id'si (numerik ama matematik değil → string). Global benzersiz.</summary>
    public string ExternalId { get; protected set; } = string.Empty;

    /// <summary>Üst kategori Trendyol id'si (kök kategorilerde null). Ağaç REST <c>parentId</c>'sinden kurulur.</summary>
    public string? ParentExternalId { get; protected set; }

    public string Name { get; protected set; } = string.Empty;

    /// <summary>Dip seviye (subCategories boş) mi — ürün yalnız buraya tanımlanır; attribute yalnız burada.</summary>
    public bool IsLeaf { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, TrendyolCategoryConsts.NameMaxLength);
    }

    public virtual void SetParent(string? parentExternalId)
    {
        ParentExternalId = string.IsNullOrWhiteSpace(parentExternalId) ? null : parentExternalId.Trim();
    }

    public virtual void SetIsLeaf(bool isLeaf)
    {
        IsLeaf = isLeaf;
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}
