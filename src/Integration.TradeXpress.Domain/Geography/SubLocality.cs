namespace Integration.TradeXpress.Geography;

/// <summary>
/// Alt-yerellik (mahalle) — <b>çekirdek HOST-GLOBAL</b> coğrafya referansı (IMultiTenant DEĞİL; TenantId yok →
/// tüm tenant'lar paylaşır; N11City deseniyle hizalı). Yerelliğe id-only bağlı
/// (<see cref="LocalityId"/>, nav YOK; aggregate sınırı). Entity şimdi tanımlanır; veri on-demand sonraki dilimde
/// (N11 GetNeighborhoods) doldurulur.
/// </summary>
public class SubLocality : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected SubLocality()
    {
    }

    public SubLocality(Guid localityId, string code, string name)
    {
        SetLocality(localityId);
        SetCode(code);
        SetName(name);
    }

    #endregion

    #region Properties

    /// <summary>Üst yerellik — id-only referans (nav YOK; aggregate sınırı). ZORUNLU.</summary>
    public virtual Guid LocalityId { get; protected set; }

    /// <summary>Kaynak kod (N11 mahalle id'si). ZORUNLU.</summary>
    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    #endregion

    #region Methods

    public virtual void SetLocality(Guid localityId)
    {
        if (localityId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:SubLocality:LocalityRequired");
        }

        LocalityId = localityId;
    }

    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeInvariantCode(code, nameof(Code), 1, GeographyConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, GeographyConsts.NameMaxLength);
    }

    public override string ToString()
    {
        return Code;
    }

    #endregion
}
