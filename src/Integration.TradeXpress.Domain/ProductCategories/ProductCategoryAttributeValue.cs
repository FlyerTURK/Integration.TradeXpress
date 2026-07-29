using Integration.TradeXpress.Variants;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Kategori niteliğinin seçilebilir bir DEĞERİ ("14K", "Kırmızı") — aggregate içi ayrı entity.
///
/// <para><b>Neden JSON değil tablo (nitelikle aynı gerekçe):</b> değer de pazaryerinin kendi değer id'sine
/// eşleştirilecek ("Kırmızı" → N11'de şu valueId). Kanal push'u yalnız niteliği değil DEĞERİ de id'siyle
/// istediğinden, eşleştirme kalıcı kimlik olmadan kurulamaz.</para>
/// </summary>
public class ProductCategoryAttributeValue : FullAuditedEntity<Guid>
{
    #region Constructors

    protected ProductCategoryAttributeValue()
    {
    }

    public ProductCategoryAttributeValue(Guid attributeId, string value, int displayOrder = 0)
    {
        AttributeId = attributeId;
        SetValue(value);
        DisplayOrder = displayOrder;
    }

    #endregion

    #region Properties

    /// <summary>Sahip nitelik (aggregate içi FK; navigation YOK).</summary>
    public virtual Guid AttributeId { get; protected set; }

    /// <summary>Değer metni — BÜYÜK/küçük harf KORUNUR ("14K", "XL" bozulmamalı), yalnız kırpılır.</summary>
    public virtual string Value { get; protected set; } = null!;

    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetValue(string value)
    {
        Value = StringFieldGuard.EnsureRequiredText(
            value, nameof(Value), 1, EntityVariantConsts.AttributeValueMaxLength);
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = order;
    }

    public override string ToString()
    {
        return Value;
    }

    #endregion
}
