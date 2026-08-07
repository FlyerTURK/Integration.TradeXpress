using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.SpecialCodes;

/// <summary>
/// Özel Kod (SpecialCode) — <b>herhangi bir entity property'sini gruplamak</b> için yönetilen, hiyerarşik kod
/// sözlüğü. Bağlam (<see cref="EntityName"/> + <see cref="PropertyName"/>) ile kapsanır: aynı sözlük yalnız o
/// (entity, property) ikilisi için geçerlidir (ör. EntityName="Good", PropertyName="Category"). Tüketen entity,
/// seçilen kodun <see cref="Code"/>'unu KENDİ mevcut string property'sinde saklar → her entity'ye FK kolonu
/// gerekmez (generic gruplama). ERPPRO <c>Sistem.OzelKod</c> (OwnerId=parent, CodeType=bağlam) ground-truth'unun
/// modern generic hali.
///
/// <para><b>Company-scoped</b> (<see cref="ICompanyScoped"/>; <see cref="CompanyId"/> nullable): null =
/// tenant-geneli / holding-host paylaşımı (tüm şirketlere görünür), dolu = o şirkete-özel. Böylece Company/Tenant
/// gibi şirkete-ait-olmayan entity'ler de gruplanabilir. Bağlam alanları (EntityName/PropertyName) set-once —
/// bir kod başka bir (entity, property)'ye TAŞINMAZ.</para>
///
/// <para><b>Hiyerarşi:</b> <see cref="ParentId"/> self-ref (aynı bağlamda üst kod); ağaç kurar. Parent aynı
/// (CompanyId, EntityName, PropertyName) bağlamında olmalı + döngü olmamalı (AppService guard).</para>
/// </summary>
public class SpecialCode : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected SpecialCode()
    {
    }

    public SpecialCode(
        string entityName,
        string propertyName,
        string code,
        string name,
        Guid? companyId = null,
        Guid? parentId = null,
        bool isActive = true)
    {
        EntityName   = ClipRequired(entityName, nameof(EntityName), SpecialCodeConsts.EntityNameMaxLength);
        PropertyName = ClipRequired(propertyName, nameof(PropertyName), SpecialCodeConsts.PropertyNameMaxLength);
        SetCode(code);
        SetName(name);
        CompanyId = companyId;
        SetParent(parentId);
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — null = tenant-geneli / holding-host (tüm şirketlere görünür), dolu = şirkete-özel.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Hedef entity tipi adı (bağlam) — set-once (ör. "Good").</summary>
    public virtual string EntityName { get; protected set; } = null!;

    /// <summary>Hedef property adı (bağlam) — set-once (ör. "Category").</summary>
    public virtual string PropertyName { get; protected set; } = null!;

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    /// <summary>Üst özel kod (aynı bağlamda) — hiyerarşi; null = kök. Self-ref, id-only.</summary>
    public virtual Guid? ParentId { get; protected set; }

    public virtual string? Description { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04); benzersizlik kontrolü AppService'te
    // (TenantId + CompanyId + EntityName + PropertyName scope).
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), SpecialCodeConsts.CodeMinLength, SpecialCodeConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, SpecialCodeConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, SpecialCodeConsts.DescriptionMaxLength);
    }

    /// <summary>Üst kodu ayarlar (aynı bağlam + döngü kontrolü AppService'te). Kendini parent yapamaz (fail-fast).</summary>
    public virtual void SetParent(Guid? parentId)
    {
        if (parentId is { } value && value == Id)
        {
            throw new BusinessException("TradeXpress:SpecialCode:CannotBeOwnParent");
        }

        ParentId = parentId == Guid.Empty ? null : parentId;
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public override string ToString()
    {
        return Code;
    }

    // Bağlam (EntityName/PropertyName) için: trim + zorunlu + max (teknik identifier; TitleCase YOK).
    private static string ClipRequired(string? value, string propertyName, int maxLength)
    {
        return StringFieldGuard.EnsureRequiredText(value, propertyName, 1, maxLength);
    }

    #endregion
}
