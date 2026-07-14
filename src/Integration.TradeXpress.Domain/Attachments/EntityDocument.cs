namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Entity-agnostik DOKÜMAN — herhangi bir entity kaydına (<see cref="EntityName"/> + <see cref="EntityId"/>) bağlı
/// yüklenmiş bir dosya eki (PDF/Word/Excel…). Owned-JSON DEĞİL, ayrı tablo (<c>AppEntityDocuments</c>): aynı yapı
/// Good/GoodVariant/Product… hepsi için kullanılır (EntityImage deseninin blob-tabanlı doküman aynası). Görselden
/// FARKI: yalnız yüklenmiş dosya (dış URL / kaynak tipi / ana-işaret YOK). İçerik blob'ta (<see cref="BlobName"/>),
/// bu satır yalnız üst-veri (orijinal ad / MIME / boyut / açıklama / sıra) taşır.
///
/// <para>Kaydetme deseni: parent formu dokümanları IN-MEMORY taşır; parent AppService kayıt sonrası
/// <c>IEntityDocumentAppService.ReplaceForAsync(EntityName, EntityId, ...)</c> ile bu tabloyu değiştirir (graph-save).</para>
/// </summary>
public class EntityDocument : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected EntityDocument()
    {
    }

    public EntityDocument(
        string entityName,
        Guid entityId,
        string fileName,
        string blobName,
        string contentType,
        long size,
        string? description,
        int displayOrder)
    {
        EntityName   = StringFieldGuard.EnsureRequiredText(entityName, nameof(EntityName), 1, EntityDocumentConsts.EntityNameMaxLength);
        SetOwner(entityId);
        FileName     = StringFieldGuard.EnsureRequiredText(fileName, nameof(FileName), 1, EntityDocumentConsts.FileNameMaxLength);
        BlobName     = StringFieldGuard.EnsureRequiredText(blobName, nameof(BlobName), 1, EntityDocumentConsts.BlobNameMaxLength);
        ContentType  = StringFieldGuard.EnsureRequiredText(contentType, nameof(ContentType), 1, EntityDocumentConsts.ContentTypeMaxLength);
        Size         = size;
        Description  = StringFieldGuard.EnsureOptionalText(description, nameof(Description), 1, EntityDocumentConsts.DescriptionMaxLength);
        DisplayOrder = displayOrder;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip entity tipi adı (bağlam) — set-once (ör. "Good", "GoodVariant").</summary>
    public virtual string EntityName { get; protected set; } = null!;

    /// <summary>Sahip kayıt id'si — set-once.</summary>
    public virtual Guid EntityId { get; protected set; }

    /// <summary>Yüklenen dosyanın orijinal adı (görüntü + indirilirken kullanılacak ad).</summary>
    public virtual string FileName { get; protected set; } = null!;

    /// <summary>Blob adı (Guid + uzantı) — içerik burada saklanır.</summary>
    public virtual string BlobName { get; protected set; } = null!;

    /// <summary>MIME tipi (ör. "application/pdf") — indirme yanıtının içerik tipi.</summary>
    public virtual string ContentType { get; protected set; } = null!;

    /// <summary>Dosya boyutu (byte).</summary>
    public virtual long Size { get; protected set; }

    /// <summary>Opsiyonel açıklama/etiket.</summary>
    public virtual string? Description { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"{EntityName}:{EntityId}:{FileName}";
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
