namespace Integration.TradeXpress.AssayOffices;

/// <summary>
/// Ayar Evi (assay office) — takoz/külçe işlemlerinde madenin saflık raporunu veren kurum (kuyumcu
/// ekosistemi referansı). <b>Company-scoped</b> katalog (çalışılan şirkete ait; <see cref="CompanyId"/> id-only,
/// nav YOK — Vault.BranchId deseni); per-tenant (IMultiTenant). Standart kimlik (Code/Name/Description/IsActive)
/// + DisplayOrder. Takoz fişinde <c>AssayOfficeId</c> ile referanslanır.
/// </summary>
public class AssayOffice : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — id-only referans (nav YOK). Kapsam DAİMA çalışılan şirket (sunucu zorlar).</summary>
    public virtual Guid CompanyId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    public virtual bool IsActive { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }

    public virtual string? Description { get; protected set; }

    protected AssayOffice() { }

    public AssayOffice(
        Guid companyId,
        string code,
        string name,
        int displayOrder = 0)
    {
        SetCompany(companyId);
        SetCode(code);
        SetName(name);
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public virtual void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:AssayOffice:CompanyRequired");
        }

        CompanyId = companyId;
    }

    public virtual void SetCode(string code)
    {
        // NormalizeCode: Trim + çoklu boşluk→tek + boşluk→'_' + UPPER, ardından zorunlu/min/max doğrulaması.
        // Elle .ToUpperInvariant() gerekmez (NormalizeCode zaten UPPER yapar).
        Code = StringFieldGuard.NormalizeCode(
            code,
            nameof(Code),
            EntityFieldConsts.CodeMinLength,
            AssayOfficeConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        // NormalizeName: Trim + çoklu boşluk→tek + TitleCase, ardından zorunlu/min/max doğrulaması.
        Name = StringFieldGuard.NormalizeName(
            name,
            nameof(Name),
            EntityFieldConsts.NameMinLength,
            AssayOfficeConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        // Opsiyonel alan: yalnız üst sınır (min yok — mevcut davranış korunur). Aşılırsa tipli Framework exception'ı.
        if (description is { Length: > AssayOfficeConsts.DescriptionMaxLength })
        {
            throw new TooLongPropertyException(nameof(Description), AssayOfficeConsts.DescriptionMaxLength);
        }

        Description = description;
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    public override string ToString()
    {
        return Code;
    }
}
