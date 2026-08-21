using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Goods;

/// <summary>
/// Good = bir <b>mamül / genel ticari mal</b> tanımı (katalog) — kuyumculuk DIŞI genel tüketim malları
/// (buzdolabı, ayakkabı, un, çikolata…). Jewelry/Stone gibi basit <b>parasal/adet</b>: milyem/işçilik/has YOK.
/// Perakende kimlik alanları (barkod/marka/model/renk/beden/cins/tür/kategori/grup) + adet takibi + fiyat tipi
/// (adet/miktar başına) + giriş/çıkış fiyatı &amp; para birimi. Alan seti ERPPRO <c>Stok.Genel</c>'den (GROUND TRUTH)
/// türetildi; KDV/ÖTV/tevkifat/tedarikçi ticari katmanı ayrı dilime ERTELENDİ.
///
/// <para><b>Şirkete AİTTİR</b> (<see cref="ICompanyOwned"/> — güvenlik sınırı, görev #4): katalog tenant-geneli
/// DEĞİL şirket kapsamlıdır; bir şirketin kullanıcısının düzenlemesi kardeş şirketleri etkilemez.
/// <see cref="CompanyId"/> ZORUNLU — sahipsiz ("holding") kayıt üretilemez; sahiplik client'tan değil aktif
/// working company'den <c>CompanyOwnershipGuard.ResolveOwnerCompanyId</c> ile yazılır.</para>
/// </summary>
public class Good : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected Good()
    {
    }

    public Good(
        string code,
        string name,
        Guid companyId,
        bool isQuantity = false,
        bool priceByQuantity = false,
        bool priceTypeChange = true,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        CompanyId        = companyId;
        IsQuantity       = isQuantity;
        PriceByQuantity  = priceByQuantity;
        PriceTypeChange  = priceTypeChange;
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }
    /// <summary>Sahip şirket — GÜVENLİK SINIRI (ICompanyOwned, ZORUNLU). Sahipsiz ("holding") emtia kaydı YOK:
    /// eskiden null olabiliyor ve tenant'ın tüm şirketlerine görünüp düzenlenebiliyordu (cross-company etki).</summary>
    public virtual Guid CompanyId { get; protected set; }
    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;
    public virtual string? Description { get; protected set; }

    // Perakende kimlik + sınıflandırma nitelikleri (serbest metin/lookup; opsiyonel) — ERPPRO Stok.Genel karşılığı.
    // Barkod ana mamülde DEĞİL → varyant seviyesinde (her SKU kendi barkodu; EntityVariant.Barcode).
    public virtual string? Brand { get; protected set; }       // Marka
    public virtual string? Model { get; protected set; }       // Model
    public virtual string? Kind { get; protected set; }        // Cins (StokCinsi)
    public virtual string? Type { get; protected set; }        // Tür (StokTuru)
    public virtual string? Color { get; protected set; }       // Renk
    public virtual string? Size { get; protected set; }        // Beden (ERPPRO int → serbest metin: "42", "XL")
    public virtual string? Category { get; protected set; }    // Kategori
    public virtual string? GroupCode { get; protected set; }   // Grup kodu

    /// <summary>Adet takibi yapılır mı (parça)?</summary>
    public virtual bool IsQuantity { get; protected set; }
    /// <summary>Fiyat adet başına mı (true) yoksa miktar/birim başına mı (false)? (FiyatTipi)</summary>
    public virtual bool PriceByQuantity { get; protected set; }
    /// <summary>Fiyat tipi fişte değiştirilebilir mi?</summary>
    public virtual bool PriceTypeChange { get; protected set; }

    /// <summary>Stok birimi (adet/kilo/cm…) — SpecialCode kodu (EntityName="Good", PropertyName="StockUnit").
    /// Ana mamül seviyesinde (tüm varyantlar aynı birim).</summary>
    public virtual string? StockUnitCode { get; protected set; }

    // ── Vergi (katalog bilgisi — % oranlar; voucher net'ine İŞLEMEZ, ayrı katman) ──
    public virtual decimal VatPurchaseRate { get; protected set; }
    public virtual decimal VatSaleRate { get; protected set; }
    public virtual decimal OtvRate { get; protected set; }
    public virtual decimal WithholdingRate { get; protected set; }

    // FİYAT (alış/kâr/satış) + Min/Max stok ana mamülde DEĞİL → VARYANT seviyesinde (GoodVariantDetail). Bir mamülün
    // "temsili fiyatı" = ANA VARYANTININ fiyatı (IGoodPricingResolver; bilanço + voucher-liste oradan besler).

    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, GoodConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, GoodConsts.DescriptionMaxLength);
    }

    public virtual void SetClassification(
        string? brand, string? model, string? kind, string? type,
        string? color, string? size, string? category, string? groupCode)
    {
        Brand     = Clip(brand, GoodConsts.AttributeMaxLength);
        Model     = Clip(model, GoodConsts.AttributeMaxLength);
        Kind      = Clip(kind, GoodConsts.AttributeMaxLength);
        Type      = Clip(type, GoodConsts.AttributeMaxLength);
        Color     = Clip(color, GoodConsts.AttributeMaxLength);
        Size      = Clip(size, GoodConsts.AttributeMaxLength);
        Category  = Clip(category, GoodConsts.AttributeMaxLength);
        GroupCode = Clip(groupCode, GoodConsts.AttributeMaxLength);
    }

    /// <summary>Fiyat tipi bayrakları (adet/miktar + fişte değişebilir).</summary>
    public virtual void SetPricingType(bool isQuantity, bool priceByQuantity, bool priceTypeChange)
    {
        IsQuantity      = isQuantity;
        PriceByQuantity = priceByQuantity;
        PriceTypeChange = priceTypeChange;
    }

    /// <summary>Stok birimini (SpecialCode kodu) atar (trim + max). Ana mamül seviyesinde (tüm varyantlar ortak).</summary>
    public virtual void SetStockUnit(string? code)
    {
        StockUnitCode = Clip(code, GoodConsts.StockUnitMaxLength);
    }

    /// <summary>Vergi oranları (% — KDV alış/satış, ÖTV, tevkifat). Her biri 0..100 (fail-fast).</summary>
    public virtual void SetTaxes(decimal vatPurchaseRate, decimal vatSaleRate, decimal otvRate, decimal withholdingRate)
    {
        EnsureRate(vatPurchaseRate);
        EnsureRate(vatSaleRate);
        EnsureRate(otvRate);
        EnsureRate(withholdingRate);

        VatPurchaseRate = vatPurchaseRate;
        VatSaleRate     = vatSaleRate;
        OtvRate         = otvRate;
        WithholdingRate = withholdingRate;
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

    private static void EnsureRate(decimal rate)
    {
        if (rate < 0m || rate > 100m)
        {
            throw new BusinessException("TradeXpress:Good:TaxRateInvalid");
        }
    }

    public override string ToString()
    {
        return Code;
    }

    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04); benzersizlik kontrolü AppService'te (TenantId+CompanyId scope).
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, GoodConsts.CodeMaxLength);
    }

    // Opsiyonel serbest-metin alanını trim'ler + üst sınıra kırpar (boş → null).
    private static string? Clip(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    #endregion
}
