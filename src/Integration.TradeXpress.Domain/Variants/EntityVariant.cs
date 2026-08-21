using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// Agnostik varyant — herhangi bir entity'ye <see cref="EntityName"/> + <see cref="EntityId"/> ile bağlı (set-once).
/// Nitelik değer KOMBİNASYONUNDAN doğar (ör. Renk=Kırmızı + Beden=42). Kod/ad OTOMATİK üretilir (senkron).
/// ORTAK alanlar (tüm entity'lerde aynı): Code/Name/IsMain/IsActive/Barcode/Stok/Açıklama. Entity-özel ZENGİN
/// alanlar (ör. Product: SalePrice/Gtin/reçete/kanal) AYRI UZANTI tablolarında (bu varyanta EntityVariantId ile bağlı) —
/// <c>EntityVariant</c> onları bilmez. Company-scoped + per-tenant. Ana varyant değişmezi (<see cref="IsMain"/>) EntityVariantManager'da.
/// </summary>
public class EntityVariant : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected EntityVariant()
    {
    }

    public EntityVariant(
        Guid? companyId, string entityName, Guid entityId, string code, string name, bool isMain = false, bool isActive = true)
    {
        CompanyId = companyId;
        SetOwner(entityName, entityId);
        SetCode(code);
        SetName(name);
        SetAsMain(isMain);
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — sahip entity'den denormalize (null = tenant-geneli). Değişmez.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Sahip entity tipi adı (ör. "Good") — set-once.</summary>
    public virtual string EntityName { get; protected set; } = null!;

    /// <summary>Sahip entity Id'si — set-once.</summary>
    public virtual Guid EntityId { get; protected set; }

    /// <summary>Ana (main) varyant mı — sahip entity başına TEKİL (invariant EntityVariantManager'da).</summary>
    public virtual bool IsMain { get; protected set; }

    /// <summary>Kombinasyondan OTOMATİK türetilen kod (benzersizlik sahip-entity scope'unda AppService/servis'te).</summary>
    public virtual string Code { get; protected set; } = null!;

    public virtual string Name { get; protected set; } = null!;

    public virtual string? Description { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    /// <summary>Barkod (varyant-başı; EAN/UPC) — opsiyonel.</summary>
    public virtual string? Barcode { get; protected set; }

    /// <summary>GTIN (Global Trade Item Number) — opsiyonel per-SKU kimliği.</summary>
    public virtual string? Gtin { get; protected set; }

    /// <summary>MPN (Manufacturer Part Number) — opsiyonel.</summary>
    public virtual string? Mpn { get; protected set; }

    /// <summary>OEM kodu — opsiyonel.</summary>
    public virtual string? Oem { get; protected set; }

    /// <summary>Stok miktarı — varsayılan 0; negatif geçersiz.</summary>
    public virtual int StockQuantity { get; protected set; }

    #endregion

    #region Methods

    // Kod OTOMATİK üretilir (kombinasyon) → min 1; UPPER + trim + max.
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), 1, EntityVariantConsts.VariantCodeMaxLength);
    }

    // Ad OTOMATİK üretilir → CASE-KORUR (combo'daki "XL" mangle olmasın), min 1.
    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(
            name, nameof(Name), 1, EntityVariantConsts.VariantNameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, EntityVariantConsts.DescriptionMaxLength);
    }

    /// <summary>Aktiflik. <b>ANA VARYANT PASİFLEŞTİRİLEMEZ</b> (2026-08-08 Hakan kuralı) — fail-fast.
    ///
    /// <para><b>Neden hata, sessiz no-op değil:</b> bu KULLANICININ bilinçli eylemidir. Sessizce yutulsaydı
    /// kullanıcı pasifleştirdiğini sanır, kayıt aktif kalır ve fark ancak ürün pazaryerinde satılmaya devam
    /// edince görülürdü. Amaç kaydı satıştan çekmekse doğru yol SAHİBİ (emtia/ürün) pasifleştirmektir —
    /// ana varyant sahibin kimliğini taşır, ondan bağımsız kapatılamaz.</para></summary>
    public virtual void SetActive(bool value)
    {
        if (!value && IsMain)
        {
            throw new BusinessException("TradeXpress:EntityVariant:MainCannotBeDeactivated")
                .WithData("Code", Code);
        }

        IsActive = value;
    }

    /// <summary>Main bayrağını değiştirir. Tekil-main değişmezi (diğerlerini düşür) EntityVariantManager'da.
    ///
    /// <para><b>Ana yapmak AKTİFLEŞTİRİR</b> — burada fail-fast YANLIŞ olurdu: bu yol sistemin yapısal
    /// onarımıdır (<c>EnsureMainVariantAsync</c> ana varyantı olmayan sahipte listedeki ilkini terfi ettirir).
    /// Tüm varyantlar pasifse fırlatmak, sahibi ANA VARYANTSIZ bırakırdı — kimlik taşıyan satır hiç olmazdı.
    /// Bu, pasif bir satırı aktifleştirmekten çok daha kötüdür. Repodaki "kendini onarır" emsaliyle aynı yön.</para></summary>
    public virtual void SetAsMain(bool value)
    {
        IsMain = value;

        if (value)
        {
            IsActive = true;   // alan üzerinden: SetActive(true) çağırmak da olurdu ama niyet burada daha açık
        }
    }

    /// <summary>Barkod (opsiyonel; boş değilse trim + max).</summary>
    public virtual void SetBarcode(string? barcode)
    {
        Barcode = StringFieldGuard.EnsureOptionalText(barcode, nameof(Barcode), 1, EntityVariantConsts.BarcodeMaxLength);
    }

    /// <summary>Ticari kimlik kodları (GTIN/MPN/OEM) — hepsi opsiyonel (boş değilse trim + max).</summary>
    public virtual void SetTradeIdentifiers(string? gtin, string? mpn, string? oem)
    {
        Gtin = StringFieldGuard.EnsureOptionalText(gtin, nameof(Gtin), 1, EntityVariantConsts.TradeIdentifierMaxLength);
        Mpn = StringFieldGuard.EnsureOptionalText(mpn, nameof(Mpn), 1, EntityVariantConsts.TradeIdentifierMaxLength);
        Oem = StringFieldGuard.EnsureOptionalText(oem, nameof(Oem), 1, EntityVariantConsts.TradeIdentifierMaxLength);
    }

    /// <summary>Stok miktarı (negatif geçersiz).</summary>
    public virtual void SetStock(int quantity)
    {
        if (quantity < 0)
        {
            throw new BusinessException("TradeXpress:EntityVariant:StockNegative");
        }

        StockQuantity = quantity;
    }

    public override string ToString()
    {
        return Code;
    }

    private void SetOwner(string entityName, Guid entityId)
    {
        EntityName = StringFieldGuard.EnsureRequiredText(
            entityName, nameof(EntityName), 1, EntityVariantConsts.EntityNameMaxLength);
        if (entityId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(EntityId));
        }

        EntityId = entityId;
    }

    #endregion
}
