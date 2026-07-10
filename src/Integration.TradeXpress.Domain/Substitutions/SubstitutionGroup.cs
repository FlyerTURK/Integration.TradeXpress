using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Substitutions;

/// <summary>
/// Muadil grubu — birbirinin yerine geçebilecek, adet-hesaplı + standart gramajlı emtiaların
/// (şimdilik Metal) SIRALI listesinin başlığı. Liste sırası = kullanıcı-kontrollü TÜKETİM ÖNCELİĞİ
/// (üsttekiler önce; zor bulunan emtia sona konur) — satırlar ayrı aggregate
/// (<see cref="SubstitutionGroupItem"/>, id-only referans; ProductAttribute/Value deseni).
/// Hesap motoru saf <see cref="SubstitutionSolver"/>'dır; grup yalnız tanım + tolerans politikası taşır.
/// <b>Company-owned</b> (Product deseni) + per-tenant. SSOT: .claude/research/muadil/konsept.md.
/// <para>Tolerans: <see cref="ToleranceValue"/>=0 → mutlak eşitlik (exact-match subset-sum);
/// &gt;0 ise türe göre gram (mutlak) ya da binde (göreceli) sapmaya izin verilir. Tolerans &gt; 0 olan
/// grupla üretilen varyant açıklamasına ticari tolerans notu M3'te otomatik iliştirilir.</para>
/// </summary>
public class SubstitutionGroup : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SubstitutionGroup() { }

    public SubstitutionGroup(
        Guid companyId,
        string code,
        string name,
        SubstitutionType type = SubstitutionType.Metal)
    {
        SetCompany(companyId);
        SetCode(code);
        SetName(name);
        SetType(type);
        SetTolerance(ToleranceType.Gram, 0m);
        IsActive = true;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — güvenlik sınırı (id-only, nav YOK). Kapsam DAİMA çalışılan şirket.</summary>
    public virtual Guid CompanyId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    public virtual string? Description { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    /// <summary>Muadil türü — şimdilik yalnız Metal; ileride Mamül vb. genişler.</summary>
    public virtual SubstitutionType Type { get; protected set; }

    /// <summary>Tolerans türü — Gram (mutlak) | PerMille (binde, göreceli).</summary>
    public virtual ToleranceType ToleranceType { get; protected set; }

    /// <summary>Tolerans değeri — varsayılan 0 = mutlak eşitlik; negatif olamaz (fail-fast).</summary>
    public virtual decimal ToleranceValue { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    // Kod DÜZENLENEBİLİR (2026-07-04 ürün kuralı). Normalize + min/max StringFieldGuard'da;
    // benzersizlik (company-scoped) AppService'te — M3.
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, SubstitutionGroupConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, SubstitutionGroupConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description,
            nameof(Description),
            EntityFieldConsts.DescriptionMinLength,
            SubstitutionGroupConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetType(SubstitutionType type)
    {
        Type = type;
    }

    /// <summary>Tolerans politikası — değer negatif olamaz; 0 = mutlak eşitlik (exact-match).</summary>
    public virtual void SetTolerance(ToleranceType toleranceType, decimal toleranceValue)
    {
        if (toleranceValue < 0m)
        {
            throw new BusinessException("TradeXpress:Substitution:ToleranceValueInvalid");
        }

        ToleranceType = toleranceType;
        ToleranceValue = toleranceValue;
    }

    public override string ToString()
    {
        return Code;
    }

    #endregion
}
