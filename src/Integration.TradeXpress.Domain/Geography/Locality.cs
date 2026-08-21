namespace Integration.TradeXpress.Geography;

/// <summary>
/// Yerellik (ilçe/şehir) — <b>core HOST-GLOBAL</b> coğrafya referansı (IMultiTenant DEĞİL; TenantId yok →
/// tüm tenant'lar paylaşır; N11City deseniyle hizalı). İdari alana id-only bağlı
/// (<see cref="AdministrativeAreaId"/>, nav YOK). <see cref="CountryId"/> denormalize (idari alandan türetilir;
/// ülke-geneli sorgu/filtreyi hızlandırır). <see cref="Code"/> kaynak kod (N11 ilçe id'si). Mahalleler
/// <see cref="SubLocality"/>'de bağlıdır.
/// </summary>
public class Locality : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected Locality()
    {
    }

    public Locality(Guid administrativeAreaId, Guid countryId, string code, string name)
    {
        SetAdministrativeArea(administrativeAreaId);
        SetCountry(countryId);
        SetCode(code);
        SetName(name);
    }

    #endregion

    #region Properties

    /// <summary>Üst idari alan — id-only referans (nav YOK; aggregate sınırı). ZORUNLU.</summary>
    public virtual Guid AdministrativeAreaId { get; protected set; }

    /// <summary>Ülke — id-only referans (denormalize; idari alandan türetilir, ülke-geneli sorguları hızlandırır). ZORUNLU.</summary>
    public virtual Guid CountryId { get; protected set; }

    /// <summary>Kaynak kod (N11 ilçe id'si). ZORUNLU.</summary>
    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    #endregion

    #region Methods

    public virtual void SetAdministrativeArea(Guid administrativeAreaId)
    {
        if (administrativeAreaId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Locality:AdministrativeAreaRequired");
        }

        AdministrativeAreaId = administrativeAreaId;
    }

    public virtual void SetCountry(Guid countryId)
    {
        if (countryId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Locality:CountryRequired");
        }

        CountryId = countryId;
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
