using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.MultiCompany;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Metals;

/// <summary>
/// Metal = bir <b>maden</b> (altın/gümüş/platin işlenmiş ürün/sikke) tanımı (katalog). Hurda'nın (<c>Scrap</c>)
/// üstüne <b>işçilik (labor)</b> ve <b>sikke/adet</b> takibi ekler. Bir ana birim (<see cref="FollowingUnitId"/>,
/// ZORUNLU; ör. HAS) + <see cref="Factor"/> (milyem; gram-altı ≤1, sikke birim-başı HAS-gram &gt;1) taşır.
///
/// <para><b>Şirkete AİTTİR</b> (<see cref="ICompanyOwned"/> — güvenlik sınırı, görev #4): katalog tenant-geneli
/// DEĞİL şirket kapsamlıdır; bir şirketin kullanıcısının düzenlemesi kardeş şirketleri etkilemez.
/// <see cref="CompanyId"/> ZORUNLU — sahipsiz ("holding") kayıt üretilemez; sahiplik client'tan değil aktif
/// working company'den damgalanır (<c>CompanyOwnershipGuard</c>).</para>
/// </summary>
public class Metal : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected Metal()
    {
    }

    public Metal(
        string code,
        string name,
        Guid followingUnitId,
        Guid companyId,
        decimal factor = MetalConsts.DefaultFactor,
        bool factorChange = false,
        bool isQuantity = false,
        decimal stableQuantity = 0m,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        SetFollowingUnit(followingUnitId);
        CompanyId = companyId;
        SetFactor(factor);
        FactorChange     = factorChange;
        IsQuantity       = isQuantity;
        StableQuantity   = stableQuantity;
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — GÜVENLİK SINIRI (ICompanyOwned, ZORUNLU). Eskiden ICompanyScoped/nullable idi:
    /// CompanyId=null "holding" kaydı tenant'ın TÜM şirketlerine görünüyor ve düzenlenebiliyordu → bir şirketin
    /// kullanıcısı kardeş şirketleri etkileyebiliyordu (cross-company manipülasyon). Artık sahipsiz emtia YOK.</summary>
    public virtual Guid CompanyId { get; protected set; }
    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;
    public virtual string? Description { get; protected set; }
    public virtual string? Barcode { get; protected set; }

    /// <summary>Madenin saf olarak dönüştüğü ana birim (FK, ZORUNLU; ör. HAS).</summary>
    public virtual Guid FollowingUnitId { get; protected set; }

    /// <summary>Milyem — gram-altı ≤1 (ör. 0.995), sikkede birim-başı HAS-gram &gt;1 (ör. 1.605). Yalnız pozitif.</summary>
    public virtual decimal Factor { get; protected set; }
    public virtual bool FactorChange { get; protected set; }

    /// <summary>Adet bazlı takip mi (sikke)?</summary>
    public virtual bool IsQuantity { get; protected set; }
    /// <summary>Adet başına sabit miktar (gram). IsQuantity + &gt;0 ise Miktar = Adet × StableQuantity.</summary>
    public virtual decimal StableQuantity { get; protected set; }

    // (İşçilik varyant detayına taşındı)


    public virtual bool IsActive { get; protected set; }

    /// <summary>Temsili görsel (owned → JSON kolonu) — TEK görsel: dış URL ya da yüklenmiş dosya (blob).
    /// Yoksa null. Bkz. <see cref="SetImage"/>/<see cref="ClearImage"/>.</summary>
    public virtual MetalImage? Image { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, MetalConsts.NameMaxLength);
    }

    public virtual void SetFollowingUnit(Guid followingUnitId)
    {
        if (followingUnitId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(FollowingUnitId));
        }

        FollowingUnitId = followingUnitId;
    }

    public virtual void SetFactor(decimal value)
    {
        // Yalnız pozitif — üst sınır yok (sikkede milyem HAS-gram olarak >1 olabilir).
        if (value <= 0m)
        {
            throw new BusinessException("TradeXpress:Metal:FactorMustBePositive");
        }

        Factor = value;
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, MetalConsts.DescriptionMaxLength);
    }

    public virtual void SetBarcode(string? barcode)
    {
        Barcode = StringFieldGuard.EnsureOptionalText(barcode, nameof(Barcode), 0, MetalConsts.BarcodeMaxLength);
    }


    public virtual void SetQuantityTracking(bool isQuantity, decimal stableQuantity)
    {
        IsQuantity     = isQuantity;
        StableQuantity = stableQuantity;
    }

    public virtual void SetFactorChange(bool value)
    {
        FactorChange = value;
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    /// <summary>Tek seferlik geçiş backfill'i (migration sonrası): <see cref="CompanyId"/> yalnız BOŞSA
    /// doldurulur. Emtianın SubAccount/Vault gibi bir PARENT'ı YOKTUR (sahibi kanıtlayan yapısal bağ yok) →
    /// sahip POLİTİKA ile seçilir: tenant'ın merkez (HQ) şirketi (bkz. <c>CompanyOwnedBackfiller</c>).
    /// Zaten doluysa DOKUNMAZ (idempotent no-op; set-once invariant korunur — Empty→değer geçişi mümkün,
    /// yeniden atama DEĞİL).</summary>
    public virtual void BackfillCompanyIfMissing(Guid companyId)
    {
        if (CompanyId != Guid.Empty)
        {
            return;
        }

        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    /// <summary>Görseli ayarlar — kaynağı boş görsel (URL'siz Url tipi / blob'suz Upload tipi / bilinmeyen tip)
    /// temizlenmiş sayılır (<see cref="ClearImage"/>). Alanlar trim'lenir; karşı kaynağın alanı taşınmaz
    /// (Url tipinde blob alanları, Upload tipinde URL null — bayat değer JSON'a persist olmasın).</summary>
    public virtual void SetImage(MetalImage? image)
    {
        if (image is null || !image.HasSource())
        {
            ClearImage();
            return;
        }

        if (image.SourceType == ProductImageSourceType.Url)
        {
            Image = new MetalImage(ProductImageSourceType.Url, image.Url!.Trim(), null, null);
            return;
        }

        Image = new MetalImage(
            ProductImageSourceType.Upload,
            null,
            image.BlobName!.Trim(),
            string.IsNullOrWhiteSpace(image.FileName) ? null : image.FileName!.Trim());
    }

    /// <summary>Görseli kaldırır (blob temizliği AppService'te — entity yalnız referansı taşır).</summary>
    public virtual void ClearImage()
    {
        Image = null;
    }

    public override string ToString()
    {
        return Code;
    }

    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04); benzersizlik kontrolü AppService'te (TenantId scope).
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, MetalConsts.CodeMaxLength);
    }

    #endregion
}
