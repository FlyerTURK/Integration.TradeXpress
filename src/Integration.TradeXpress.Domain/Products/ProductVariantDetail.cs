using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Bir varyantın PRODUCT-ÖZEL detayı — jenerik <c>EntityVariant</c>'ın Product uzantısı (1:1, <see cref="EntityVariantId"/>
/// set-once). Satış/liste fiyatı (marketplace price/optionPrice) VARYANT seviyesinde; reçete satırları ayrı entity olarak
/// <c>EntityVariantId</c>'ye bağlanır. Company-scoped (varyanttan denormalize) + per-tenant. Jenerik <c>EntityVariant</c>
/// bu uzantıyı BİLMEZ — sahip (ProductAppService) EntityVariantId ile eşleyip saklar/yükler (<c>GoodVariantDetail</c> deseni).
/// </summary>
public class ProductVariantDetail : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected ProductVariantDetail()
    {
    }

    public ProductVariantDetail(Guid? companyId, Guid entityVariantId)
    {
        CompanyId = companyId;
        SetVariant(entityVariantId);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — varyanttan denormalize (null = tenant-geneli). Değişmez.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Detaylandırdığı jenerik varyant — id-only, set-once (1:1).</summary>
    public virtual Guid EntityVariantId { get; protected set; }

    /// <summary>Satış/liste fiyatı (marketplace price/optionPrice). Null = fiyatlanmamış (henüz listeye hazır değil).</summary>
    public virtual decimal? SalePrice { get; protected set; }

    /// <summary>Satış fiyatı para birimi (CurrencyUnit id-only; N11'e currencyType'a eşlenir). Fiyat null ise null.</summary>
    public virtual Guid? SalePriceCurrencyUnitId { get; protected set; }

    /// <summary>
    /// Bu varyantın PAKET DESİSİ — kargo tarifesinin girdisi. <c>null</c> = kanalın varsayılan desisi kullanılır
    /// (<c>SalesChannelBase.DefaultPackageDesi</c>); yani alan yalnız İSTİSNA için doldurulur.
    /// <para><b>Neden en/boy/yükseklik değil:</b> desi = hacim/bölen formülü üç alan + kullanıcı disiplini ister,
    /// üstelik bölen (3000/4000) pazaryerine göre değişir. Kullanıcı zaten kutusunu bilir; doğrudan desi girmek
    /// hem az veri hem tartışmasız sonuç (2026-07-27 Hakan kararı: "ürüne göre çok değişiyor").</para>
    /// <para>0 = "Dosya" satırı (pazaryerinin ağırlıksız/küçük gönderi basamağı) — geçerli bir değerdir.</para>
    /// </summary>
    public virtual int? PackageDesi { get; protected set; }

    /// <summary>Varyantın PAZARYERİ satılabilirlik durumu (2026-08-05 Hakan kararı). Varsayılan
    /// <see cref="ProductSaleStatus.Draft"/> — fail-closed: doğrulanmamış varyant push aday listesine GİRMEZ.
    /// <c>IsActive</c>'in yerine geçmez, yanında durur (bkz. <see cref="ProductSaleStatus"/>).</summary>
    public virtual ProductSaleStatus SaleStatus { get; protected set; }

    /// <summary>Doğrulamanın yapıldığı an (UTC). null = hiç doğrulanmadı.</summary>
    public virtual DateTime? VerifiedAt { get; protected set; }

    /// <summary>Doğrulayan kullanıcı — "kim onayladı" sorusunun cevabı denetim izidir.</summary>
    public virtual Guid? VerifiedBy { get; protected set; }

    /// <summary>
    /// Onay anındaki REÇETE STAMP'İ — onayın hâlâ geçerli olup olmadığını anlamanın anahtarı.
    ///
    /// <para><b>Neden stamp:</b> onay bir kereye mahsus tik olursa emniyet değil SÜS olur; reçete sonradan
    /// değişir ve kimse fark etmez. Stamp push anında yeniden hesaplanıp karşılaştırılır; tutmuyorsa varyant
    /// doğrulanmamış sayılır. Böylece reçeteye dokunan herkes onayı düşürmüş olur ve <b>ayrı olay altyapısı
    /// gerekmez</b>.</para>
    ///
    /// <para><b>İKİ KADEMELİ</b> (2026-08-05 kararı): <c>"{en son değişim ticks}|{içerik hash'i}"</c>.
    /// Önce zaman kısmı kıyaslanır (ucuz); değişmişse içerik hash'i bakılır. Salt timestamp
    /// dokunulup aynı bırakılan satırda YANLIŞ POZİTİF üretir; salt içerik hash'i ise sıralama/yuvarlama/null
    /// detaylarında sessizce yanlış olabilir. İkisi birlikte ikisinin de zayıflığını kapatır.</para>
    /// </summary>
    public virtual string? VerifiedRecipeStamp { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Satış fiyatı + para birimi (fiyat null → para birimi de null). Negatif fiyat geçersiz (fail-fast).</summary>
    public virtual void SetSalePrice(decimal? price, Guid? currencyUnitId)
    {
        if (price is { } value && value < 0m)
        {
            throw new BusinessException("TradeXpress:Product:SalePriceNegative");
        }

        SalePrice = price;
        SalePriceCurrencyUnitId = price is null ? null : currencyUnitId;
    }

    /// <summary>Paket desisini set eder. <c>null</c> = kanal varsayılanına dön. Negatif desi yoktur (fail-fast);
    /// 0 geçerlidir ("Dosya" basamağı).</summary>
    public virtual void SetPackageDesi(int? packageDesi)
    {
        if (packageDesi is { } value && value < 0)
        {
            throw new BusinessException("TradeXpress:Product:PackageDesiNegative");
        }

        PackageDesi = packageDesi;
    }

    /// <summary>İNSAN yolu: varyantı doğrular → <see cref="ProductSaleStatus.Ready"/>. Onay anındaki reçete
    /// stamp'i saklanır. <see cref="ProductSaleStatus.Closed"/>'dan da çıkarır — kullanıcı kapattığını
    /// yeniden açabilir.</summary>
    public virtual void MarkVerified(string recipeStamp, DateTime verifiedAtUtc, Guid? verifiedBy)
    {
        SaleStatus = ProductSaleStatus.Ready;
        VerifiedRecipeStamp = recipeStamp;
        VerifiedAt = verifiedAtUtc;
        VerifiedBy = verifiedBy;
    }

    /// <summary>SİSTEM yolu: yalnız <see cref="ProductSaleStatus.Ready"/> olanı askıya alır.
    ///
    /// <para><b>Neden yalnız Ready:</b> <c>Draft</c> zaten satışta değil, <c>Closed</c> ise KULLANICININ
    /// kararıdır — sistemin onu "askıya alınmış" diye yeniden sınıflandırması kullanıcının niyetini ezerdi.
    /// Diğer durumlarda sessizce no-op (idempotent: aynı emtia iki kez pasifleşse de sonuç aynı).</para>
    ///
    /// <para>Ters yön (<c>Suspended → Ready</c>) burada YOKTUR ve olmayacaktır — geri dönüş yalnız
    /// <see cref="MarkVerified"/> ile, yani insandan geçer.</para></summary>
    public virtual void Suspend()
    {
        if (SaleStatus != ProductSaleStatus.Ready)
        {
            return;
        }

        SaleStatus = ProductSaleStatus.Suspended;
    }

    /// <summary>İNSAN yolu: varyantı satıştan çeker.</summary>
    public virtual void Close()
    {
        SaleStatus = ProductSaleStatus.Closed;
    }

    /// <summary>Verilen stamp onay anındakiyle aynı mı — yani onay HÂLÂ geçerli mi.
    /// Doğrulanmamış varyantta daima <c>false</c> (stamp yok, karşılaştıracak bir şey de yok).</summary>
    public virtual bool IsVerificationCurrent(string currentRecipeStamp)
    {
        return SaleStatus == ProductSaleStatus.Ready
               && VerifiedRecipeStamp != null
               && string.Equals(VerifiedRecipeStamp, currentRecipeStamp, StringComparison.Ordinal);
    }

    public override string ToString()
    {
        return EntityVariantId.ToString();
    }

    private void SetVariant(Guid entityVariantId)
    {
        if (entityVariantId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(EntityVariantId));
        }

        EntityVariantId = entityVariantId;
    }

    #endregion
}
