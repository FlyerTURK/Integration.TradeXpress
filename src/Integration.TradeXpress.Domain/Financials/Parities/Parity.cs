namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Bir döviz/maden ÇİFTİ (parite panosu satırı): <b>1 Base = X Quote</b>. Yön çift
/// konvansiyonudur (<see cref="CurrencyUnitPriority"/>): yüksek öncelikli birim Base
/// (USDTRY→base USD, EURUSD→base EUR, HASUSD→base HAS).
///
/// <para>Parity yalnız <b>çift tanımıdır</b> — oran burada saklanmaz, kendi marjı YOKTUR.
/// Canlı oran okuma anında birimlerin <b>efektif fiyatının saf çaprazından</b> türetilir.
/// Base/Quote id-only referans (nav YOK — aggregate sınırı). "System" saklanmaz: <c>TenantId==null</c>
/// (host/global) ≡ sistem; tenant kendi paritesini ekleyebilir.</para>
/// </summary>
public class Parity : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected Parity()
    {
    }

    public Parity(
        Guid baseCurrencyUnitId,
        Guid quoteCurrencyUnitId,
        bool isActive = true,
        int displayOrder = 999)
    {
        if (baseCurrencyUnitId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(BaseCurrencyUnitId));
        }

        if (quoteCurrencyUnitId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(QuoteCurrencyUnitId));
        }

        if (baseCurrencyUnitId == quoteCurrencyUnitId)
        {
            throw new BusinessException("TradeXpress:Parity:BaseQuoteMustDiffer");
        }

        BaseCurrencyUnitId = baseCurrencyUnitId;
        QuoteCurrencyUnitId = quoteCurrencyUnitId;
        IsActive = isActive;
        SetDisplayOrder(displayOrder);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Çiftin base'i (1 base = X quote) — yüksek öncelikli birim.</summary>
    public virtual Guid BaseCurrencyUnitId { get; protected set; }

    /// <summary>Çiftin karşı (quote) birimi.</summary>
    public virtual Guid QuoteCurrencyUnitId { get; protected set; }

    public virtual bool IsActive { get; protected set; }
    public virtual int DisplayOrder { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = StringFieldGuard.EnsureRange(
            order,
            nameof(DisplayOrder),
            EntityFieldConsts.DisplayOrderMin,
            ParityConsts.DisplayOrderMax);
    }

    public override string ToString()
    {
        return $"{BaseCurrencyUnitId} / {QuoteCurrencyUnitId}";
    }

    #endregion
}
