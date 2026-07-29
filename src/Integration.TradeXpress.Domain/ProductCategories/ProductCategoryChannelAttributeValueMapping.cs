using Integration.Framework;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Çekirdek nitelik DEĞERİNİN bir kanaldaki karşılığı ("22K" → N11'in değer listesindeki "22 Ayar" kaydı).
/// Nitelik eşleştirmesinin bir alt katmanıdır.
///
/// <para><b>Neden ayrı bir katman gerekiyor:</b> pazaryerleri çoğu nitelikte SERBEST METİN kabul etmez, kendi
/// değer listelerinden KİMLİK bekler. Nitelik eşleşse bile değer eşleşmezse ürün "geçersiz değer" diye
/// reddedilir; ada göre gönderim de tutmaz ("22K" ≠ "22 Ayar").</para>
///
/// <para><b>Neden kanal niteliğine değil kategoriye asılı:</b> değer eşleştirmesi kategori bağlamında
/// anlamlıdır (aynı "Ayar" niteliği farklı kategorilerde farklı değer kümesi sunabilir) ve kategori
/// eşleştirmesiyle birlikte silinmesi gerekir.</para>
/// </summary>
public class ProductCategoryChannelAttributeValueMapping : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected ProductCategoryChannelAttributeValueMapping()
    {
    }

    public ProductCategoryChannelAttributeValueMapping(
        Guid companyId,
        Guid productCategoryId,
        SalesChannelType channel,
        Guid productCategoryAttributeValueId,
        string channelAttributeValueExternalId,
        string? channelAttributeValueName)
    {
        CompanyId = companyId;
        ProductCategoryId = productCategoryId;
        Channel = channel;
        ProductCategoryAttributeValueId = productCategoryAttributeValueId;
        SetChannelValue(channelAttributeValueExternalId, channelAttributeValueName);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — kategoriden denormalize. Değişmez.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Eşleştirmenin tanımlandığı kategori — id-only, set-once.</summary>
    public virtual Guid ProductCategoryId { get; protected set; }

    public virtual SalesChannelType Channel { get; protected set; }

    /// <summary>Çekirdek nitelik DEĞERİ — SAHİBİ kategorideki kalıcı kimlik. Ada değil kimliğe bağlanır ki
    /// değer yeniden adlandırıldığında eşleştirme kopmasın.</summary>
    public virtual Guid ProductCategoryAttributeValueId { get; protected set; }

    /// <summary>Kanaldaki değer kimliği (N11 valueId / Trendyol attributeValueId / Etsy value id).</summary>
    public virtual string ChannelAttributeValueExternalId { get; protected set; } = null!;

    /// <summary>Kanal değerinin adı — salt GÖSTERİM (bayatlayabilir; doğruluk kimlikte).</summary>
    public virtual string? ChannelAttributeValueName { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetChannelValue(string channelAttributeValueExternalId, string? channelAttributeValueName)
    {
        ChannelAttributeValueExternalId = StringFieldGuard.NormalizeName(
            channelAttributeValueExternalId,
            nameof(ChannelAttributeValueExternalId),
            1,
            ProductCategoryChannelMappingConsts.ChannelAttributeIdMaxLength);
        ChannelAttributeValueName = channelAttributeValueName?.Trim();
    }

    public override string ToString()
    {
        return ChannelAttributeValueName ?? ChannelAttributeValueExternalId;
    }

    #endregion
}
