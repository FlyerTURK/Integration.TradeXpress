using Integration.Framework;
using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürünün GENEL ÖZELLİĞİ — kategoriden gelen bir spesifikasyon niteliğinin BU ÜRÜNDEKİ değeri
/// ("Ayar: 22K", "Gramaj: 8.4", "Marka: X"). Pazaryeri push'unda kanal kategorisinin nitelik değerlerini
/// (N11 <c>productAttribute</c>, Trendyol/Etsy karşılıkları) besler.
///
/// <para><b>Varyant ekseninden FARKLIDIR:</b> varyant nitelikleri (Renk/Beden) kartezyen üretip ayrı varyantlar
/// doğurur; spesifikasyon ürünün tamamını niteler ve varyant üretimine HİÇ girmez. İkisini aynı yerde tutmak,
/// "Ayar" yazan kullanıcıya farkında olmadan varyant patlaması yaşatırdı.</para>
///
/// <para><b>Neden ürüne yazılıyor, kategoride durmuyor:</b> aynı kategoride 14K ve 22K ürünler bir arada satılır —
/// ayar ürünün özelliğidir, kategorinin değil. Kategori yalnız hangi özelliklerin sorulacağını (ve varsa önerilen
/// değerleri) tanımlar.</para>
///
/// <para><b>Neden ayrı entity (JSON değil):</b> "22K olan ürünler" gibi sorgular ve pazaryeri nitelik
/// eşleştirmesi bu satırlara dayanacak; JSON kolonunda ikisi de filtrelenemez hâle gelirdi.</para>
///
/// <para>Nitelik bağı <see cref="ProductCategoryAttributeId"/> ile KALICI kimliğe kuruludur (ada değil):
/// kategoride nitelik yeniden adlandırıldığında ürünlerin değerleri kopmaz.</para>
/// </summary>
public class ProductSpecification : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected ProductSpecification()
    {
    }

    public ProductSpecification(Guid? companyId, Guid productId, Guid productCategoryAttributeId, string? value)
    {
        CompanyId = companyId;
        ProductId = productId;
        ProductCategoryAttributeId = productCategoryAttributeId;
        SetValue(value);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — üründen denormalize. Değişmez.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Sahip ürün — id-only (aggregate'ler arası referans), set-once.</summary>
    public virtual Guid ProductId { get; protected set; }

    /// <summary>Değerlendiği kategori niteliği — SAHİBİ kategorideki kalıcı kimlik (devralınmışsa üst kategoriye
    /// aittir). Ada değil kimliğe bağlanır ki yeniden adlandırma değerleri koparmasın.</summary>
    public virtual Guid ProductCategoryAttributeId { get; protected set; }

    /// <summary>Bu üründeki değer ("22K"). Kategoride tanımlı değerlerden biri olabilir ya da serbest metin —
    /// kategori değer listesi ÖNERİDİR, kısıt değil (pazaryerleri de çoğu nitelikte serbest girişe izin verir).</summary>
    public virtual string Value { get; protected set; } = null!;

    #endregion

    #region Methods

    public virtual void SetValue(string? value)
    {
        Value = StringFieldGuard.NormalizeName(
            value, nameof(Value), 1, ProductConsts.SpecificationValueMaxLength);
    }

    public override string ToString()
    {
        return Value;
    }

    #endregion
}
