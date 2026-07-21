using Integration.Framework.Addressing;

namespace Integration.TradeXpress.Shipments;

/// <summary>
/// Birleşik kargo/teslimat şablonu — <b>ERP-level, kanal-nötr çekirdek</b> (SSOT, yeniden kullanılabilir).
/// Kullanıcı tanımlar (sync DEĞİL); ürünler <c>Product.ShipmentTemplateId</c> ile referans eder (id-only, nav YOK).
/// <b>Company-owned</b> güvenlik sınırı (<see cref="ICompanyOwned"/>, non-null <see cref="CompanyId"/>) + per-tenant.
/// Standart kimlik (Code/Name/Description/IsActive) + menşei adresi (zorunlu) + hazırlık/teslim süresi + ücret modeli
/// (ücretsiz/alıcı-öder/şartlı) + iade + max satın alım. Kanallar (N11/Etsy/Trendyol) bu çekirdeği kendi kodlamalarına
/// eşler (SONRAKİ FAZLAR). Silme referans bütünlüğü AppService silme-guard'ıyla korunur (sert FK/cascade DEĞİL).
/// </summary>
public class ShipmentTemplate : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected ShipmentTemplate()
    {
    }

    public ShipmentTemplate(
        Guid companyId,
        string code,
        string name,
        Address originAddress,
        int processingDaysMin,
        int processingDaysMax)
    {
        SetCompany(companyId);
        SetCode(code);
        SetName(name);
        SetOrigin(originAddress);
        SetProcessingDays(processingDaysMin, processingDaysMax);
        FeeModel = ShipmentFeeModel.Free;
        IsActive = true;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — id-only referans (company-owned; oluşturmadan sonra değişmez).</summary>
    public virtual Guid CompanyId { get; protected set; }

    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    public virtual string? Description { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    /// <summary>Menşei/gönderici (depo) adresi — ZORUNLU. Yeniden-kullanılabilir <see cref="Address"/> VO (OwnsOne).</summary>
    public virtual Address OriginAddress { get; protected set; } = null!;

    /// <summary>Hazırlık/işleme süresi alt sınırı (gün) — en az 1.</summary>
    public virtual int ProcessingDaysMin { get; protected set; }

    /// <summary>Hazırlık/işleme süresi üst sınırı (gün) — <see cref="ProcessingDaysMin"/>'den küçük olamaz.</summary>
    public virtual int ProcessingDaysMax { get; protected set; }

    /// <summary>Kargo ücret modeli (ücretsiz/alıcı-öder/şartlı).</summary>
    public virtual ShipmentFeeModel FeeModel { get; protected set; }

    /// <summary>Şartlı kargo eşiği (bu tutar/adet üzeri ücretsiz). Yalnız <see cref="ShipmentFeeModel.Conditional"/>'da dolu.</summary>
    public virtual decimal? ConditionalThreshold { get; protected set; }

    /// <summary>Şartlı kargo eşiğinin birimi (tutar/adet). Yalnız <see cref="ShipmentFeeModel.Conditional"/>'da dolu.</summary>
    public virtual ShipmentConditionalUnit? ConditionalUnit { get; protected set; }

    /// <summary>Teslim süresi alt sınırı (gün) — opsiyonel; doluysa en az 1.</summary>
    public virtual int? DeliveryDaysMin { get; protected set; }

    /// <summary>Teslim süresi üst sınırı (gün) — opsiyonel; ikisi de doluysa <see cref="DeliveryDaysMin"/>'den küçük olamaz.</summary>
    public virtual int? DeliveryDaysMax { get; protected set; }

    /// <summary>Kargo firması — çekirdek <see cref="Carrier"/> kataloğuna id-only referans (opsiyonel; SSOT).
    /// Sert FK yok (Carrier host-global). <see cref="CarrierName"/> bununla birlikte denormalize snapshot tutulur.</summary>
    public virtual Guid? CarrierId { get; protected set; }

    /// <summary>Kargo firması adı — <see cref="CarrierId"/>'den türeyen denormalize snapshot (opsiyonel). Picker
    /// seçince çözülen firma adıyla dolar; legacy serbest-metin kayıtlarda tek başına kalabilir.</summary>
    public virtual string? CarrierName { get; protected set; }

    /// <summary>İade kabul ediliyor mu.</summary>
    public virtual bool ReturnAccepted { get; protected set; }

    /// <summary>İade adresi — opsiyonel (OwnsOne). İade kapalıysa temizlenir.</summary>
    public virtual Address? ReturnAddress { get; protected set; }

    /// <summary>İade koşulları/açıklaması — opsiyonel. İade kapalıysa temizlenir.</summary>
    public virtual string? ReturnInfo { get; protected set; }

    /// <summary>Alıcı başına maksimum satın alım adedi (opsiyonel) — en az 1.</summary>
    public virtual int? MaxPurchaseQuantity { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetCode(string code)
    {
        // NormalizeCode: Trim + çoklu boşluk→tek + UPPER (boşluk KORUNUR) + zorunlu/min/max.
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, ShipmentTemplateConsts.CodeMaxLength);
    }

    public virtual void SetName(string name)
    {
        // NormalizeName: Trim + çoklu boşluk→tek + TitleCase + zorunlu/min/max.
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, ShipmentTemplateConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, ShipmentTemplateConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    /// <summary>Menşei (gönderici) adresini ayarlar — zorunlu (null → fail-fast).</summary>
    public virtual void SetOrigin(Address originAddress)
    {
        if (originAddress is null)
        {
            throw new RequiredPropertyException(nameof(OriginAddress));
        }

        OriginAddress = originAddress;
    }

    /// <summary>Hazırlık süresi aralığını ayarlar. Alt sınır en az 1; üst sınır alt sınırdan küçük olamaz (fail-fast).</summary>
    public virtual void SetProcessingDays(int min, int max)
    {
        if (min < 1)
        {
            throw new BusinessException("TradeXpress:Shipment:Template:ProcessingDaysInvalid");
        }

        if (max < min)
        {
            throw new BusinessException("TradeXpress:Shipment:Template:ProcessingDaysInvalid");
        }

        ProcessingDaysMin = min;
        ProcessingDaysMax = max;
    }

    /// <summary>Ücret modelini ayarlar. Şartlı ise eşik &gt; 0 + birim ZORUNLU; değilse ikisi de temizlenir (fail-fast).</summary>
    public virtual void SetFee(ShipmentFeeModel model, decimal? threshold, ShipmentConditionalUnit? unit)
    {
        if (model == ShipmentFeeModel.Conditional)
        {
            if (threshold is not { } value || value <= 0)
            {
                throw new BusinessException("TradeXpress:Shipment:Template:FeeConditionalInvalid");
            }

            if (unit is not { } conditionalUnit)
            {
                throw new BusinessException("TradeXpress:Shipment:Template:FeeConditionalInvalid");
            }

            FeeModel = ShipmentFeeModel.Conditional;
            ConditionalThreshold = value;
            ConditionalUnit = conditionalUnit;
            return;
        }

        FeeModel = model;
        ConditionalThreshold = null;
        ConditionalUnit = null;
    }

    /// <summary>Teslim süresi aralığını ayarlar (opsiyonel). Dolu değerler en az 1; ikisi de doluysa alt ≤ üst (fail-fast).</summary>
    public virtual void SetDeliveryDays(int? min, int? max)
    {
        if (min is { } lo && lo < 1)
        {
            throw new BusinessException("TradeXpress:Shipment:Template:DeliveryDaysInvalid");
        }

        if (max is { } hi && hi < 1)
        {
            throw new BusinessException("TradeXpress:Shipment:Template:DeliveryDaysInvalid");
        }

        if (min is { } a && max is { } b && a > b)
        {
            throw new BusinessException("TradeXpress:Shipment:Template:DeliveryDaysInvalid");
        }

        DeliveryDaysMin = min;
        DeliveryDaysMax = max;
    }

    /// <summary>Kargo firmasını ayarlar — id (çekirdek <see cref="Carrier"/> referansı, SSOT) + denormalize ad
    /// snapshot'ı ATOMİK. İkisi de opsiyonel; ad boş değilse trim + max (guard korunur). id null ise firma
    /// temizlenir (id + ad birlikte).</summary>
    public virtual void SetCarrier(Guid? carrierId, string? carrierName)
    {
        CarrierId = carrierId;
        CarrierName = StringFieldGuard.EnsureOptionalText(
            carrierName, nameof(CarrierName), 1, ShipmentTemplateConsts.CarrierNameMaxLength);
    }

    /// <summary>İade bilgisini ayarlar. İade kapalıysa adres + açıklama TEMİZLENİR (tutarlılık); açıksa opsiyonel geçer.</summary>
    public virtual void SetReturn(bool accepted, Address? returnAddress, string? returnInfo)
    {
        ReturnAccepted = accepted;

        if (!accepted)
        {
            ReturnAddress = null;
            ReturnInfo = null;
            return;
        }

        ReturnAddress = returnAddress;
        ReturnInfo = StringFieldGuard.EnsureOptionalText(
            returnInfo, nameof(ReturnInfo), 1, ShipmentTemplateConsts.ReturnInfoMaxLength);
    }

    /// <summary>Alıcı başına maksimum satın alım adedi (opsiyonel) — en az 1 (fail-fast).</summary>
    public virtual void SetMaxPurchaseQuantity(int? maxPurchaseQuantity)
    {
        if (maxPurchaseQuantity is { } value && value < 1)
        {
            throw new BusinessException("TradeXpress:Shipment:Template:MaxPurchaseQuantityInvalid");
        }

        MaxPurchaseQuantity = maxPurchaseQuantity;
    }

    public override string ToString()
    {
        return Code;
    }

    // Company set-once (oluşturmada) → public mutator YOK; yalnız ctor.
    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    #endregion
}
