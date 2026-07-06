using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Satış kanalı tanımının <b>soyut TPT tabanı</b> (Table-Per-Type): ortak kimlik + sahiplik alanları burada,
/// pazaryeri-özel yapılandırma (API kimlik bilgileri vb.) somut alt-tiplerde (ör. <see cref="SalesChannelTrN11"/>).
/// Base tablo <c>AppSalesChannels</c>; her alt-tip kendi tablosunu ekler (paylaşılan PK/FK).
///
/// <para><b>Company-owned</b> güvenlik sınırı (<see cref="ICompanyOwned"/>, non-nullable <see cref="CompanyId"/>)
/// + per-tenant (<see cref="IMultiTenant"/>): her şirketin kendi kanalları; "tüm şirketlere açık" hâli YOKTUR.
/// Kapsam DAİMA çalışılan şirket (sunucu <c>ICurrentCompany</c> ile zorlar; client CompanyId göndermez).</para>
/// </summary>
public abstract class SalesChannelBase : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelBase()
    {
    }

    protected SalesChannelBase(
        Guid companyId,
        string code,
        string name,
        bool isActive = true)
    {
        SetCompany(companyId);
        SetCode(code);
        SetName(name);
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı (id-only, nav YOK). Oluşturmadan sonra değişmez (set-once).</summary>
    public virtual Guid CompanyId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;
    public virtual string? Description { get; protected set; }
    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name,
            nameof(Name),
            EntityFieldConsts.NameMinLength,
            SalesChannelConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description,
            nameof(Description),
            EntityFieldConsts.DescriptionMinLength,
            SalesChannelConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public override string ToString()
    {
        return Code;
    }

    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04); benzersizlik kontrolü AppService'te (TenantId+CompanyId scope).
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code,
            nameof(Code),
            EntityFieldConsts.CodeMinLength,
            SalesChannelConsts.CodeMaxLength);
    }

    // Company set-once (oluşturmada) → public mutator YOK; yalnız ctor.
    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    #endregion
}
