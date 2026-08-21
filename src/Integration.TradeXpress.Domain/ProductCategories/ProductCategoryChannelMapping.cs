using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Core kategorinin bir SATIŞ KANALINDAKİ karşılığı — "bizim <c>Takı › Yüzük › Alyans</c> kategorimiz
/// N11'de şu kategoridir" eşleştirmesi. Company-owned (her şirket kendi taksonomisini kendi eşleştirir).
///
/// <para><b>Neden var (2026-07-27 Hakan vizyonu):</b> ürün core kategoriye bağlandığında kanal kategorisi
/// artık her kanalda ELLE seçilmez; kanalın kategori KOMİSYONU da bu eşleştirmeden çözülüp reçeteye GrossUp maliyet
/// olarak girer. Bugün komisyon yalnız N11 KANAL ÜRÜNÜ oluşturulduktan sonra biliniyordu; bu eşleştirmeyle
/// ürünün kendi reçetesinde de bilinir.</para>
///
/// <para><b>Kanal kategorisi METİN kimlikle tutulur</b> (<see cref="ChannelCategoryExternalId"/>): kanal
/// taksonomileri (N11/Trendyol/Etsy) kendi dış id'lerini string verir ve bunlar host-global tablolarda yaşar
/// (<c>N11Category.ExternalId</c>). Sert FK YOK — taksonomi yeniden senkronlandığında eşleştirme kırılmasın,
/// yalnız çözümlenemez hâle gelsin (fail-soft: komisyon boş kalır, satır üretilmez).</para>
///
/// <para><b>YAPRAK ZORUNLU DEĞİL</b> (2026-07-27 Hakan): bizim bir ARA kategorimiz kanalın FİNAL kategorisine
/// denk gelebilir; tersi de mümkündür. Bu yüzden ne bizim tarafta ne kanal tarafında "yaprak olmalı" kısıtı vardır.</para>
/// </summary>
public class ProductCategoryChannelMapping : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected ProductCategoryChannelMapping()
    {
    }

    public ProductCategoryChannelMapping(
        Guid companyId,
        Guid productCategoryId,
        SalesChannelType channel,
        string channelCategoryExternalId)
    {
        SetCompany(companyId);
        SetProductCategory(productCategoryId);
        Channel = channel;
        SetChannelCategory(channelCategoryExternalId, null);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — set-once (company-owned).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Çekirdek kategori — id-only, set-once (eşleştirmenin kimliği bu çift üzerinden kurulur).</summary>
    public virtual Guid ProductCategoryId { get; protected set; }

    /// <summary>Hangi kanal ailesi (N11 / Trendyol / Etsy) — set-once.</summary>
    public virtual SalesChannelType Channel { get; protected set; }

    /// <summary>Kanal kategorisinin DIŞ kimliği (ör. N11 kategori id'si). Sert FK yok — bkz. sınıf özeti.</summary>
    public virtual string ChannelCategoryExternalId { get; protected set; } = null!;

    /// <summary>Kanal kategori adının okunabilirlik SNAPSHOT'ı (yol dahil olabilir). Doğruluk kimlikte;
    /// bu alan bayatlarsa yalnız gösterim etkilenir.</summary>
    public virtual string? ChannelCategoryName { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Kanal kategorisini (kimlik + gösterim adı) değiştirir — kullanıcı başka bir kanal kategorisi seçtiğinde.</summary>
    public virtual void SetChannelCategory(string channelCategoryExternalId, string? channelCategoryName)
    {
        ChannelCategoryExternalId = StringFieldGuard.EnsureRequiredText(
            channelCategoryExternalId,
            nameof(ChannelCategoryExternalId),
            1,
            ProductCategoryChannelMappingConsts.ChannelCategoryIdMaxLength);

        // Kanal ADI serbest snapshot'tır (kanal ne verdiyse o) — min uzunluk dayatmayız, yalnız üst sınır:
        // kısa bir kanal adı ("Ev") yüzünden eşleştirme kaydedilememesi anlamsız olurdu.
        ChannelCategoryName = StringFieldGuard.EnsureOptionalText(
            channelCategoryName,
            nameof(ChannelCategoryName),
            minLength: 0,
            ProductCategoryChannelMappingConsts.ChannelCategoryNameMaxLength);
    }

    public override string ToString()
    {
        return $"{Channel}:{ChannelCategoryExternalId}";
    }

    // Şirket set-once → public mutator YOK; yalnız ctor.
    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    // Çekirdek kategori bağı set-once → eşleştirme başka kategoriye TAŞINMAZ (silinip yenisi kurulur);
    // taşınabilir olsaydı kimlik çifti (kategori, kanal) altından kayardı.
    private void SetProductCategory(Guid productCategoryId)
    {
        if (productCategoryId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(ProductCategoryId));
        }

        ProductCategoryId = productCategoryId;
    }

    #endregion
}
