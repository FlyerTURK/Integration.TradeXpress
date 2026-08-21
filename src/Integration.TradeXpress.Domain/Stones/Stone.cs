using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Stones;

/// <summary>
/// Stone = bir <b>değerli taş</b> tanımı (katalog). Maden/Hurda'dan çok daha basit: milyem/işçilik/has YOK,
/// tamamen <b>parasal/adet</b>. Tanım alanları (cins/tür/renk/kesim/saflık/elek/kategori/grup) + adet takibi +
/// fiyat tipi (adet/miktar başına) + giriş/çıkış fiyatı &amp; para birimi taşır.
///
/// <para><b>Şirkete AİTTİR</b> (<see cref="ICompanyOwned"/> — güvenlik sınırı, görev #4): katalog tenant-geneli
/// DEĞİL şirket kapsamlıdır; bir şirketin kullanıcısının düzenlemesi kardeş şirketleri etkilemez.
/// <see cref="CompanyId"/> ZORUNLU — sahipsiz ("holding") kayıt üretilemez; sahiplik client'tan değil aktif
/// working company'den <c>CompanyOwnershipGuard.ResolveOwnerCompanyId</c> ile yazılır.</para>
/// </summary>
public class Stone : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected Stone()
    {
    }

    public Stone(
        string code,
        string name,
        Guid companyId,
        bool isQuantity = false,
        bool priceByQuantity = false,
        bool priceTypeChange = true,
        decimal entryPrice = 0m,
        Guid? entryPriceUnitId = null,
        decimal exitPrice = 0m,
        Guid? exitPriceUnitId = null,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        CompanyId        = companyId;
        IsQuantity       = isQuantity;
        PriceByQuantity  = priceByQuantity;
        PriceTypeChange  = priceTypeChange;
        EntryPrice       = entryPrice;
        EntryPriceUnitId = entryPriceUnitId;
        ExitPrice        = exitPrice;
        ExitPriceUnitId  = exitPriceUnitId;
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

    // Tanım nitelikleri (serbest metin/lookup; opsiyonel)
    public virtual string? StoneKind { get; protected set; }   // Cins (elmas/yakut/safir…)
    public virtual string? StoneType { get; protected set; }   // Tür (doğal/sentetik…)
    public virtual string? Color { get; protected set; }       // Renk
    public virtual string? Cut { get; protected set; }         // Kesim
    public virtual string? Clarity { get; protected set; }     // Saflık/berraklık
    public virtual string? Sieve { get; protected set; }       // Elek
    public virtual string? Category { get; protected set; }    // Kategori
    public virtual string? GroupCode { get; protected set; }   // Grup kodu

    /// <summary>Adet takibi yapılır mı (sikke/parça)?</summary>
    public virtual bool IsQuantity { get; protected set; }
    /// <summary>Fiyat adet başına mı (true) yoksa miktar/gram başına mı (false)? (FiyatTipi)</summary>
    public virtual bool PriceByQuantity { get; protected set; }
    /// <summary>Fiyat tipi fişte değiştirilebilir mi?</summary>
    public virtual bool PriceTypeChange { get; protected set; }

    public virtual decimal EntryPrice { get; protected set; }
    public virtual Guid? EntryPriceUnitId { get; protected set; }
    public virtual decimal ExitPrice { get; protected set; }
    public virtual Guid? ExitPriceUnitId { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name, nameof(Name), EntityFieldConsts.NameMinLength, StoneConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), EntityFieldConsts.DescriptionMinLength, StoneConsts.DescriptionMaxLength);
    }

    public virtual void SetAttributes(
        string? stoneKind, string? stoneType, string? color, string? cut,
        string? clarity, string? sieve, string? category, string? groupCode)
    {
        StoneKind = Trim(stoneKind);
        StoneType = Trim(stoneType);
        Color     = Trim(color);
        Cut       = Trim(cut);
        Clarity   = Trim(clarity);
        Sieve     = Trim(sieve);
        Category  = Trim(category);
        GroupCode = Trim(groupCode);

        static string? Trim(string? v)
        {
            var t = v?.Trim();
            return string.IsNullOrEmpty(t) ? null : t.Length > StoneConsts.AttributeMaxLength ? t[..StoneConsts.AttributeMaxLength] : t;
        }
    }

    public virtual void SetPricing(
        bool isQuantity, bool priceByQuantity, bool priceTypeChange,
        decimal entryPrice, Guid? entryPriceUnitId, decimal exitPrice, Guid? exitPriceUnitId)
    {
        IsQuantity       = isQuantity;
        PriceByQuantity  = priceByQuantity;
        PriceTypeChange  = priceTypeChange;
        EntryPrice       = entryPrice;
        EntryPriceUnitId = entryPriceUnitId;
        ExitPrice        = exitPrice;
        ExitPriceUnitId  = exitPriceUnitId;
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

    public override string ToString()
    {
        return Code;
    }

    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04); benzersizlik kontrolü AppService'te (TenantId+CompanyId scope).
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code, nameof(Code), EntityFieldConsts.CodeMinLength, StoneConsts.CodeMaxLength);
    }

    #endregion
}
