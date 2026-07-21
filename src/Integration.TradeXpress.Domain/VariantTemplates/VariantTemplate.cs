using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.VariantTemplates;

/// <summary>
/// Varyant tanım ŞABLONU (demet) — yeniden kullanılabilir <b>katalog</b>: bir kez tanımlanan özellik grupları +
/// değerleri (ör. "Renk" = {Kırmızı, Siyah}, "Beden" = {S, M, L}). Ürünün "Özellikleri Düzenle" popup'ında
/// "Katalogtan Uygula" ile seçilir → ürünün agnostik nitelik grafına (EntityAttribute/Value) KOPYALANIR → varyantlar
/// üretilir. Böylece aynı gruplar her üründe elle tekrar girilmez. <b>Company-owned</b> (güvenlik sınırı;
/// <see cref="CompanyId"/> non-null <see cref="ICompanyOwned"/>) + per-tenant. Gruplar+değerler owned → JSON
/// (<see cref="Attributes"/>; self-contained kompozisyon, dışarıdan referanslanmaz).
/// </summary>
public class VariantTemplate : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected VariantTemplate()
    {
    }

    public VariantTemplate(Guid companyId, string code, string name, int displayOrder = 0)
    {
        SetCompany(companyId);
        SetCode(code);
        SetName(name);
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — id-only referans (company-owned; oluşturmadan sonra değişmez).</summary>
    public virtual Guid CompanyId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    public virtual string? Description { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }

    /// <summary>Şablonun özellik grupları (owned → JSON; her grup adı + değerleri). Demet = bu grupların tümü.</summary>
    public virtual List<VariantTemplateAttribute> Attributes { get; protected set; } = new();

    #endregion

    #region Methods

    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, VariantTemplateConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, VariantTemplateConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, VariantTemplateConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    /// <summary>Özellik gruplarını ayarlar — yalnız adı DOLU gruplar; her grupta yalnız değeri dolu satırlar; sıralı;
    /// ad/değer trim. Boş grup/değer elenir (agnostik EntityAttribute SetName davranışıyla hizalı).</summary>
    public virtual void SetAttributes(IEnumerable<VariantTemplateAttribute>? attributes)
    {
        Attributes = (attributes ?? Enumerable.Empty<VariantTemplateAttribute>())
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .OrderBy(a => a.DisplayOrder)
            .Select(a => new VariantTemplateAttribute(
                a.Name.Trim(),
                a.DisplayOrder,
                (a.Values ?? new List<VariantTemplateAttributeValue>())
                    .Where(v => !string.IsNullOrWhiteSpace(v.Value))
                    .OrderBy(v => v.DisplayOrder)
                    .Select(v => new VariantTemplateAttributeValue(v.Value.Trim(), v.DisplayOrder))
                    .ToList()))
            .ToList();
    }

    public override string ToString()
    {
        return Code;
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
