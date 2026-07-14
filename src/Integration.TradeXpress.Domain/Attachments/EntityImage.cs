using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Entity-agnostik GÖRSEL — herhangi bir entity kaydına (<see cref="EntityName"/> + <see cref="EntityId"/>) bağlı
/// bir görsel. Owned-JSON DEĞİL, ayrı tablo (<c>AppEntityImages</c>): aynı yapı Good/GoodVariant/Product/Metal…
/// hepsi için kullanılır (SpecialCode/Document deseninde entity-agnostik reusable blok). İki kaynak: dış URL ya da
/// yüklenmiş dosya (blob). Sıra <see cref="DisplayOrder"/> (küçük önce), ilk/işaretli = ana görsel
/// (<see cref="IsDefault"/>). Kaynak tipi ortak <see cref="ProductImageSourceType"/> (E1e'de agnostik ada taşınacak).
///
/// <para>Kaydetme deseni: parent formu görselleri IN-MEMORY taşır; parent AppService kayıt sonrası
/// <c>IEntityImageAppService.ReplaceForAsync(EntityName, EntityId, ...)</c> ile bu tabloyu değiştirir (graph-save).</para>
/// </summary>
public class EntityImage : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected EntityImage()
    {
    }

    public EntityImage(
        string entityName,
        Guid entityId,
        ProductImageSourceType sourceType,
        string? url,
        string? blobName,
        string? fileName,
        int displayOrder,
        bool isDefault)
    {
        EntityName   = StringFieldGuard.EnsureRequiredText(entityName, nameof(EntityName), 1, EntityImageConsts.EntityNameMaxLength);
        SetOwner(entityId);
        SourceType   = sourceType;
        Url          = url;
        BlobName     = blobName;
        FileName     = fileName;
        DisplayOrder = displayOrder;
        IsDefault    = isDefault;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip entity tipi adı (bağlam) — set-once (ör. "Good", "GoodVariant").</summary>
    public virtual string EntityName { get; protected set; } = null!;

    /// <summary>Sahip kayıt id'si — set-once.</summary>
    public virtual Guid EntityId { get; protected set; }

    public virtual ProductImageSourceType SourceType { get; protected set; }

    /// <summary>Dış görsel bağlantısı — yalnız <see cref="ProductImageSourceType.Url"/> kaynağında dolu.</summary>
    public virtual string? Url { get; protected set; }

    /// <summary>Blob adı (Guid + uzantı) — yalnız <see cref="ProductImageSourceType.Upload"/> kaynağında dolu.</summary>
    public virtual string? BlobName { get; protected set; }

    /// <summary>Yüklenen dosyanın orijinal adı (görüntü/teşhis) — Upload kaynağında dolu.</summary>
    public virtual string? FileName { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }

    /// <summary>Varsayılan (ana) görsel — tekil-default garantisi AppService normalize'ında.</summary>
    public virtual bool IsDefault { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Kaynağı gerçekten dolu mu — Url tipinde URL, Upload tipinde blob adı ister (bilinmeyen tip = boş).</summary>
    public virtual bool HasSource()
    {
        if (SourceType == ProductImageSourceType.Url)
        {
            return !string.IsNullOrWhiteSpace(Url);
        }

        if (SourceType == ProductImageSourceType.Upload)
        {
            return !string.IsNullOrWhiteSpace(BlobName);
        }

        return false;
    }

    public override string ToString()
    {
        return $"{EntityName}:{EntityId}";
    }

    private void SetOwner(Guid entityId)
    {
        if (entityId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(EntityId));
        }

        EntityId = entityId;
    }

    #endregion
}
