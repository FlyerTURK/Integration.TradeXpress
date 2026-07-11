using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Kanalın <b>tek gider satırı</b> (owned VO; <see cref="SideCostSettings.Items"/> listesinde JSON'da yaşar) —
/// ürün reçetesi grid'i tarzı gider satırları modeli (2026-07-10 kullanıcı kararı). Eski sabit-alanlı
/// SideCostSettings + SideCostPostingTarget BURADA birleşti: kalem türü + hesaplama + değer + fiş hedefi TEK satır.
///
/// <para><b>Fiş hizalaması:</b> her kalem yalnız fiyat girdisi DEĞİL — satış gerçekleştiğinde GERÇEK finansal
/// olaydır (komisyon kesintisi, kargo/Loomis ödemesi). <see cref="ServiceId"/> = Service KATALOĞUNA id-only
/// referans (composer reçete satırının CommodityId'sine koyar); <see cref="PostingMode"/> +
/// <see cref="AccountId"/>/<see cref="SubAccountId"/> = ileride sipariş→fiş akışında VoucherLine'ın karşı
/// carisi. BU DİLİMDE FİŞ YAZILMAZ — yalnız veri bağı kurulur.</para>
///
/// <para><b>AutoRate (YALNIZ Commission):</b> N11'de efektif oran kategoriden çözülür
/// (<c>ResolveEffectiveCommissionRate</c> SSOT), <see cref="Value"/> fallback'tir. Trendyol/Etsy'de kapalı —
/// oran doğrudan <see cref="Value"/>.</para>
///
/// <para><b>RequiresVariantOptIn:</b> sigortalı-gönderim (Loomis) deseninin genelleştirilmesi — işaretli kalem
/// yalnız VARYANT bazında anahtar açıksa (StockItem.InsuredShippingEnabled) reçeteye uygulanır.</para>
/// </summary>
public class SideCostItem
{
    #region Constructors

    protected SideCostItem()
    {
    }

    public SideCostItem(
        SideCostKind kind,
        string? displayName,
        SideCostCalcMode calcMode,
        decimal value,
        Guid? currencyUnitId,
        Guid? serviceId,
        SideCostPostingMode postingMode,
        Guid? accountId,
        Guid? subAccountId,
        bool autoRate,
        bool isEnabled,
        int displayOrder,
        bool requiresVariantOptIn)
    {
        EnsureValueValid(kind, calcMode, value);

        // AutoRate yalnız komisyon kaleminde anlamlı (kategori/kanal oran çözümü) — diğerlerinde fail-fast.
        if (autoRate && kind != SideCostKind.Commission)
        {
            throw new BusinessException("TradeXpress:SalesChannel:SideCostAutoRateOnlyForCommission");
        }

        // Opt-in kalem GrossUp olamaz: composer tüm GrossUp oranlarını TEK birleşik satırda toplar ve satırın
        // türünü BİRİNCİL kalemden alır — farklı türde opt-in GrossUp karışırsa varyant toggle senkronu
        // (SyncVariantOptInLines tür-bazlı düşürür/üretir) birleşik satırı bulamaz, bayat operand kalır
        // (ON'da eksik, OFF'ta fazla fiyat — SESSİZ). Bilinen iş kuralı da yok: Loomis primi PercentOfCost,
        // komisyon varyant-bazlı değil → fail-fast.
        if (requiresVariantOptIn && calcMode == SideCostCalcMode.GrossUpPercent)
        {
            throw new BusinessException("TradeXpress:SalesChannel:SideCostOptInGrossUpNotSupported");
        }

        // Alt hesap ancak ana hesap varken anlamlı (Voucher hesap deseni) — yetim SubAccount fail-fast.
        if (subAccountId is not null && accountId is null)
        {
            throw new BusinessException("TradeXpress:SalesChannel:SideCostSubAccountWithoutAccount");
        }

        Kind = kind;
        DisplayName = StringFieldGuard.EnsureOptionalText(
            displayName, nameof(DisplayName), minLength: 1, SalesChannelConsts.SideCostDisplayNameMaxLength);
        CalcMode = calcMode;
        Value = value;

        // Para birimi yalnız sabit tutarda anlamlı (mutlak tutarın birimi); yüzde modlarında sessizce temizlenir.
        CurrencyUnitId = calcMode == SideCostCalcMode.FixedAmount && currencyUnitId != Guid.Empty
            ? currencyUnitId
            : null;

        ServiceId = serviceId == Guid.Empty ? null : serviceId;
        PostingMode = postingMode;
        AccountId = accountId == Guid.Empty ? null : accountId;
        SubAccountId = subAccountId == Guid.Empty ? null : subAccountId;
        AutoRate = autoRate;
        IsEnabled = isEnabled;
        DisplayOrder = StringFieldGuard.EnsureRange(
            displayOrder, nameof(DisplayOrder), EntityFieldConsts.DisplayOrderMin, EntityFieldConsts.DisplayOrderMax);
        RequiresVariantOptIn = requiresVariantOptIn;
    }

    #endregion

    #region Properties

    /// <summary>Kalem türü — reçete satırındaki idempotent reconcile ANAHTARI (<c>SideCostKind</c> kolonu).</summary>
    public virtual SideCostKind Kind { get; protected set; }

    /// <summary>Serbest görünen ad — boşsa UI türün lokalizesini gösterir (ör. "Offsite Ads").</summary>
    public virtual string? DisplayName { get; protected set; }

    /// <summary>Hesaplama modu — reçeteye düz projeksiyon (Add / Percent / GrossUp).</summary>
    public virtual SideCostCalcMode CalcMode { get; protected set; }

    /// <summary>Tutar ya da oran — moda göre yorumlanır (FixedAmount: tutar ≥ 0; PercentOfCost: 0-100;
    /// GrossUpPercent: [0,100)). Komisyonda <see cref="AutoRate"/> açıkken fallback oran.</summary>
    public virtual decimal Value { get; protected set; }

    /// <summary>Sabit tutarın para birimi — id-only; null = kanal yerel birimi. Yalnız FixedAmount'ta dolu.</summary>
    public virtual Guid? CurrencyUnitId { get; protected set; }

    /// <summary>Hizmet kartı (Service kataloğu) — id-only, opsiyonel; reçete satırının Service etiketi de bu olur.</summary>
    public virtual Guid? ServiceId { get; protected set; }

    /// <summary>Fişleme hedefi (karşı cari / genel gider).</summary>
    public virtual SideCostPostingMode PostingMode { get; protected set; }

    /// <summary>Karşı taraf cari hesabı — id-only, opsiyonel; yalnız <see cref="SideCostPostingMode.CounterpartyAccount"/>'ta anlamlı.</summary>
    public virtual Guid? AccountId { get; protected set; }

    /// <summary>Karşı taraf alt hesabı — id-only, opsiyonel (Voucher.SubAccountId paritesi; ana hesapsız olamaz).</summary>
    public virtual Guid? SubAccountId { get; protected set; }

    /// <summary>Oran otomatik çözülsün mü — YALNIZ Commission (N11: kategori efektif oranı; Value = fallback).</summary>
    public virtual bool AutoRate { get; protected set; }

    /// <summary>Kalem aktif mi — kapalı kalem reçeteye satır üretmez (grid'de satır durur, veri kaybolmaz).</summary>
    public virtual bool IsEnabled { get; protected set; }

    /// <summary>Grid/reçete sırası — GrossUp kalemleri sıradan bağımsız HEP EN SONA projeksiyonlanır (motor kuralı).</summary>
    public virtual int DisplayOrder { get; protected set; }

    /// <summary>Yalnız varyantta anahtar açıksa uygulanır (Loomis/sigortalı-gönderim deseninin genellemesi).</summary>
    public virtual bool RequiresVariantOptIn { get; protected set; }

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"{Kind} {CalcMode}={Value}";
    }

    // Değer guard'ı moda göre: sabit tutar negatif olamaz; Percent 0-100; GrossUp [0,100) (payda pozitif kalmalı).
    // Komisyon kalemi ZORUNLU GrossUp (sabit giderler komisyona tabi — kâr korunumu matematiği).
    private static void EnsureValueValid(SideCostKind kind, SideCostCalcMode calcMode, decimal value)
    {
        if (kind == SideCostKind.Commission && calcMode != SideCostCalcMode.GrossUpPercent)
        {
            throw new BusinessException("TradeXpress:SalesChannel:SideCostCommissionRequiresGrossUp");
        }

        if (calcMode == SideCostCalcMode.FixedAmount && value < 0m)
        {
            throw new BusinessException("TradeXpress:SalesChannel:SideCostAmountNegative")
                .WithData("property", nameof(Value));
        }

        if (calcMode == SideCostCalcMode.PercentOfCost && (value < 0m || value > 100m))
        {
            throw new BusinessException("TradeXpress:SalesChannel:SideCostRateOutOfRange")
                .WithData("property", nameof(Value));
        }

        if (calcMode == SideCostCalcMode.GrossUpPercent
            && (value < 0m || value >= ProductRecipeConsts.GrossUpOperandExclusiveMax))
        {
            throw new BusinessException("TradeXpress:SalesChannel:SideCostRateOutOfRange")
                .WithData("property", nameof(Value));
        }
    }

    #endregion
}
