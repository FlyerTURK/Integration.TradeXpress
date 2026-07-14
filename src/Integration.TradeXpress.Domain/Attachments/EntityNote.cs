namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Entity-agnostik NOT — herhangi bir entity kaydına (<see cref="EntityName"/> + <see cref="EntityId"/>) bağlı sade
/// metin not (opsiyonel başlık + zorunlu metin). Owned-JSON DEĞİL, ayrı tablo (<c>AppEntityNotes</c>): aynı yapı
/// Good/GoodVariant/Product… hepsi için (EntityImage/EntityDocument deseninin en sade aynası — blob YOK). Kim/ne zaman
/// bilgisi <see cref="FullAuditedAggregateRoot{TKey}"/>'tan (CreatorId/CreationTime) gelir.
///
/// <para>Kaydetme deseni: parent formu notları IN-MEMORY taşır; parent AppService kayıt sonrası
/// <c>IEntityNoteAppService.ReplaceForAsync(EntityName, EntityId, ...)</c> ile bu tabloyu değiştirir (graph-save).</para>
/// </summary>
public class EntityNote : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected EntityNote()
    {
    }

    public EntityNote(
        string entityName,
        Guid entityId,
        string? title,
        string text,
        int displayOrder)
    {
        EntityName   = StringFieldGuard.EnsureRequiredText(entityName, nameof(EntityName), 1, EntityNoteConsts.EntityNameMaxLength);
        SetOwner(entityId);
        Title        = StringFieldGuard.EnsureOptionalText(title, nameof(Title), 1, EntityNoteConsts.TitleMaxLength);
        Text         = StringFieldGuard.EnsureRequiredText(text, nameof(Text), 1, EntityNoteConsts.TextMaxLength);
        DisplayOrder = displayOrder;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip entity tipi adı (bağlam) — set-once (ör. "Good").</summary>
    public virtual string EntityName { get; protected set; } = null!;

    /// <summary>Sahip kayıt id'si — set-once.</summary>
    public virtual Guid EntityId { get; protected set; }

    /// <summary>Opsiyonel başlık.</summary>
    public virtual string? Title { get; protected set; }

    /// <summary>Not metni — zorunlu.</summary>
    public virtual string Text { get; protected set; } = null!;

    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetTitle(string? title)
    {
        Title = StringFieldGuard.EnsureOptionalText(title, nameof(Title), 1, EntityNoteConsts.TitleMaxLength);
    }

    public virtual void SetText(string text)
    {
        Text = StringFieldGuard.EnsureRequiredText(text, nameof(Text), 1, EntityNoteConsts.TextMaxLength);
    }

    public virtual void SetDisplayOrder(int displayOrder)
    {
        DisplayOrder = displayOrder;
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
