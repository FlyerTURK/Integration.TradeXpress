using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Varyant ↔ attribute-değer bağı — bir <see cref="ProductVariant"/>'ın bir attribute için SEÇİLİ değeri.
/// Varyantın kombinasyon kimliğini kurar: her attribute'tan bir satır ({Renk:Kırmızı},{Beden:M}…). Kombinasyon
/// (varyantın tüm <see cref="ProductAttributeValueId"/> kümesi) ürün içinde BENZERSİZDİR (üretim/senkron API/Domain
/// tarafında). <see cref="ProductAttributeId"/> denormalize tutulur → "varyant başına attribute başına TEK değer"
/// değişmezi tek unique index'le (VariantId, AttributeId) zorlanır. <b>Company-owned</b> (denormalize) + per-tenant.
/// Tümü set-once (kombinasyon bağı değişmez; değişiklik = varyant yeniden üretimi).
/// </summary>
public class ProductVariantAttributeValue : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — denormalize (güvenlik sınırı). Değişmez.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip varyant — id-only referans. Değişmez.</summary>
    public virtual Guid ProductVariantId { get; protected set; }

    /// <summary>Attribute — id-only (denormalize, "attribute başına tek değer" unique index'i için). Değişmez.</summary>
    public virtual Guid ProductAttributeId { get; protected set; }

    /// <summary>Seçili attribute değeri — id-only referans. Değişmez.</summary>
    public virtual Guid ProductAttributeValueId { get; protected set; }

    protected ProductVariantAttributeValue() { }

    public ProductVariantAttributeValue(
        Guid companyId,
        Guid productVariantId,
        Guid productAttributeId,
        Guid productAttributeValueId)
    {
        SetCompany(companyId);
        ProductVariantId = Require(productVariantId, nameof(ProductVariantId));
        ProductAttributeId = Require(productAttributeId, nameof(ProductAttributeId));
        ProductAttributeValueId = Require(productAttributeValueId, nameof(ProductAttributeValueId));
    }

    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    private static Guid Require(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new RequiredPropertyException(name);
        }

        return id;
    }
}
