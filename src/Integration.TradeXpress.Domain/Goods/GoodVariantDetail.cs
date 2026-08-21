using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Goods;

/// <summary>
/// Bir varyantın GOOD-ÖZEL fiyat/stok detayı — jenerik <c>EntityVariant</c>'ın Good uzantısı (1:1, <see cref="EntityVariantId"/>
/// set-once). Perakende fiyat/stok VARYANT seviyesinde (2026-07-13 kullanıcı kararı: fiyat/stok Good'dan varyanta taşındı):
/// alış (fiyat/birim/vergi) → kâr şekli (Margin) → türetilmiş satış (ExitPrice) + stok birimi/adet-bazlı/min-max.
/// Company-scoped (varyanttan denormalize) + per-tenant. Jenerik <c>EntityVariant</c> bu uzantıyı BİLMEZ —
/// sahip (GoodAppService) EntityVariantId ile eşleyip saklar/yükler (uzantı mekanizması; Faz C'de Product da kullanır).
/// </summary>
public class GoodVariantDetail : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyScoped
{
    #region Constructors

    protected GoodVariantDetail()
    {
    }

    public GoodVariantDetail(Guid? companyId, Guid entityVariantId)
    {
        CompanyId = companyId;
        SetVariant(entityVariantId);
        Margin = MarginSetting.Passthrough;   // kâr şekli varsayılan: ×1 (satış = alış)
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — varyanttan denormalize (null = tenant-geneli). Değişmez.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Detaylandırdığı jenerik varyant — id-only, set-once (1:1).</summary>
    public virtual Guid EntityVariantId { get; protected set; }

    // ── Stok yapılandırması ──
    /// <summary>Stok birimi (adet/kilo/cm…) — SpecialCode kodu.</summary>
    public virtual string? StockUnitCode { get; protected set; }
    /// <summary>Adet-bazlı mı (parça)?</summary>
    public virtual bool IsQuantity { get; protected set; }
    /// <summary>Min stok (uyarı eşiği) — opsiyonel; negatif geçersiz.</summary>
    public virtual decimal? MinQuantity { get; protected set; }
    /// <summary>Max stok — opsiyonel; negatif geçersiz; ikisi doluysa Min ≤ Max.</summary>
    public virtual decimal? MaxQuantity { get; protected set; }

    // ── Fiyat ──
    public virtual decimal EntryPrice { get; protected set; }
    public virtual Guid? EntryPriceUnitId { get; protected set; }
    /// <summary>Alış fiyatı KDV DAHİL mi.</summary>
    public virtual bool EntryPriceTaxIncluded { get; protected set; }

    /// <summary>Satış fiyatı — TÜRETİLMİŞ (<see cref="Margin"/>.Apply(<see cref="EntryPrice"/>)); elle GİRİLMEZ.</summary>
    public virtual decimal ExitPrice { get; protected set; }
    /// <summary>Satış fiyatı para birimi — alış birimiyle AYNI (türetilmiş).</summary>
    public virtual Guid? ExitPriceUnitId { get; protected set; }
    /// <summary>Satış fiyatı KDV DAHİL mi (bilgi).</summary>
    public virtual bool ExitPriceTaxIncluded { get; protected set; }

    /// <summary>Kâr şekli (owned VO — MarginType + değer). Satış fiyatını alıştan TÜRETİR.</summary>
    public virtual MarginSetting Margin { get; protected set; } = null!;

    #endregion

    #region Methods

    /// <summary>Stok birimini (SpecialCode kodu) atar (trim + max).</summary>
    public virtual void SetStockUnit(string? code)
    {
        StockUnitCode = Clip(code, GoodConsts.StockUnitMaxLength);
    }

    /// <summary>Adet-bazlı bayrağı.</summary>
    public virtual void SetIsQuantity(bool isQuantity)
    {
        IsQuantity = isQuantity;
    }

    /// <summary>Min/Max stok — opsiyonel; negatif geçersiz; ikisi doluysa Min ≤ Max (fail-fast).</summary>
    public virtual void SetQuantityLimits(decimal? minQuantity, decimal? maxQuantity)
    {
        if (minQuantity < 0m || maxQuantity < 0m)
        {
            throw new BusinessException("TradeXpress:Good:QuantityNegative");
        }

        if (minQuantity is { } min && maxQuantity is { } max && min > max)
        {
            throw new BusinessException("TradeXpress:Good:MinMaxInvalid");
        }

        MinQuantity = minQuantity;
        MaxQuantity = maxQuantity;
    }

    /// <summary>Alış fiyatı + birim + KDV-dahil. Satış fiyatını yeniden türetir (marj × alış).</summary>
    public virtual void SetPurchasePrice(decimal entryPrice, Guid? entryPriceUnitId, bool taxIncluded)
    {
        if (entryPrice < 0m)
        {
            throw new BusinessException("TradeXpress:Good:PriceNegative");
        }

        EntryPrice = entryPrice;
        EntryPriceUnitId = entryPriceUnitId == Guid.Empty ? null : entryPriceUnitId;
        EntryPriceTaxIncluded = taxIncluded;
        RecomputeExitPrice();
    }

    /// <summary>Kâr şekli (MarginType + değer) + satış KDV-dahil bilgisi. Satış fiyatını yeniden türetir.</summary>
    public virtual void SetMargin(MarginSetting margin, bool saleTaxIncluded)
    {
        Margin = margin ?? MarginSetting.Passthrough;
        ExitPriceTaxIncluded = saleTaxIncluded;
        RecomputeExitPrice();
    }

    /// <summary>Satış fiyatı para birimi — YALNIZ Sabit Fiyat (FinalPrice) marjında BAĞIMSIZ set edilir (mutlak fiyat,
    /// alıştan türemez). Diğer marjlarda satış birimi = alış birimi (RecomputeExitPrice zorlar).</summary>
    public virtual void SetSalePriceUnit(Guid? unitId)
    {
        ExitPriceUnitId = unitId == Guid.Empty ? null : unitId;
    }

    // Satış fiyatını alıştan türetir: ExitPrice = Margin.Apply(EntryPrice). Sabit Fiyat DIŞINDA birim alışla aynı;
    // Sabit Fiyatta birim bağımsızdır (SetSalePriceUnit) — burada ezilmez.
    private void RecomputeExitPrice()
    {
        var margin = Margin ?? MarginSetting.Passthrough;
        ExitPrice = margin.Apply(EntryPrice);
        if (margin.Type != MarginType.FinalPrice)
        {
            ExitPriceUnitId = EntryPriceUnitId;
        }
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
