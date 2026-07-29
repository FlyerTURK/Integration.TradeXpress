using Integration.Framework;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Çekirdek kategori NİTELİĞİNİN bir kanaldaki karşılığı ("Ayar" → N11'in "Maden Ayarı" niteliği).
/// Kategori eşleştirmesinin bir alt katmanıdır: kategori hangi kanal kategorisine gideceğini, bu satırlar da
/// o kategorinin hangi niteliğine hangi değerin yazılacağını söyler.
///
/// <para><b>Neden ada göre otomatik eşleşme yetmez:</b> pazaryerleri aynı kavramı farklı adlandırır
/// ("Ayar" ≠ "Maden Ayarı", "Gramaj" ≠ "Toplam Gram"). Ada güvenilseydi eşleşmeyen nitelik sessizce
/// gönderilmez, ürün de zorunlu nitelik eksik diye pazaryerinde reddedilirdi.</para>
///
/// <para><b>Kalıtım YOK (kategori eşleştirmesinden farklı):</b> nitelikler kategori zincirinden zaten
/// devralınıyor; eşleştirme ise niteliğin KALICI kimliğine bağlanır. Üst kategoride tanımlı bir nitelik için
/// yapılan eşleştirme, o niteliği devralan tüm alt kategorilerde aynı kimlikle bulunur.</para>
/// </summary>
public class ProductCategoryChannelAttributeMapping : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected ProductCategoryChannelAttributeMapping()
    {
    }

    public ProductCategoryChannelAttributeMapping(
        Guid companyId,
        Guid productCategoryId,
        SalesChannelType channel,
        Guid productCategoryAttributeId,
        string channelAttributeExternalId,
        string? channelAttributeName)
    {
        CompanyId = companyId;
        ProductCategoryId = productCategoryId;
        Channel = channel;
        ProductCategoryAttributeId = productCategoryAttributeId;
        SetChannelAttribute(channelAttributeExternalId, channelAttributeName);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — kategoriden denormalize. Değişmez.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Eşleştirmenin tanımlandığı kategori — id-only, set-once.</summary>
    public virtual Guid ProductCategoryId { get; protected set; }

    public virtual SalesChannelType Channel { get; protected set; }

    /// <summary>Çekirdek nitelik — SAHİBİ kategorideki kalıcı kimlik (devralınmışsa üst kategoriye aittir).</summary>
    public virtual Guid ProductCategoryAttributeId { get; protected set; }

    /// <summary>Kanaldaki nitelik kimliği (N11 attribute id / Trendyol attributeId / Etsy property id).
    /// Metin tutulur: üç kanalın kimlik tipi farklı (sayısal/metin) ve tek kolonda taşınması gerekiyor.</summary>
    public virtual string ChannelAttributeExternalId { get; protected set; } = null!;

    /// <summary>Kanal niteliğinin adı — salt GÖSTERİM (bayatlayabilir; kimlik tek doğru kaynaktır).</summary>
    public virtual string? ChannelAttributeName { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetChannelAttribute(string channelAttributeExternalId, string? channelAttributeName)
    {
        ChannelAttributeExternalId = StringFieldGuard.NormalizeName(
            channelAttributeExternalId,
            nameof(ChannelAttributeExternalId),
            1,
            ProductCategoryChannelMappingConsts.ChannelAttributeIdMaxLength);
        ChannelAttributeName = channelAttributeName?.Trim();
    }

    public override string ToString()
    {
        return ChannelAttributeName ?? ChannelAttributeExternalId;
    }

    #endregion
}
