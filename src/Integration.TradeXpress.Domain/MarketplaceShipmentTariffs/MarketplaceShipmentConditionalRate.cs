namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>
/// Şartlı kargo baremi: sepet tutarı bu aralıktayken (ve gönderi <c>ConditionalMaxDesi</c> altındayken)
/// desi tarifesi YERİNE uygulanan SABİT ücret. N11 yayını: "300 TL altı siparişleriniz için … kargo firması
/// bazlı sabit kargo ücretleri uygulanacaktır" (0–149,99 ve 149,99–299,99 dilimleri).
/// <para>Kuyumda sepet neredeyse hep 300 TL üstü olduğundan pratikte devreye girmez — yine de saklanır:
/// kaynağa sadakat, ileride başka ürün grubu satılırsa hazır.</para>
/// <para>Owned tip (JSON): kanal başına 2 satır, sorgulanmaz.</para>
/// </summary>
public class MarketplaceShipmentConditionalRate
{
    #region Constructors

    protected MarketplaceShipmentConditionalRate()
    {
    }

    public MarketplaceShipmentConditionalRate(decimal basketFrom, decimal? basketTo, decimal amount)
    {
        if (basketFrom < 0m)
        {
            throw new BusinessException("TradeXpress:ShipmentTariff:ConditionalBasketFromNegative");
        }

        if (basketTo is { } upper && upper <= basketFrom)
        {
            throw new BusinessException("TradeXpress:ShipmentTariff:ConditionalBasketRangeInvalid");
        }

        if (amount < 0m)
        {
            throw new BusinessException("TradeXpress:ShipmentTariff:ConditionalAmountNegative");
        }

        BasketFrom = basketFrom;
        BasketTo = basketTo;
        Amount = amount;
    }

    #endregion

    #region Properties

    /// <summary>Dilimin alt sınırı (dahil).</summary>
    public virtual decimal BasketFrom { get; protected set; }

    /// <summary>Dilimin üst sınırı (hariç). <c>null</c> = üst sınırsız.</summary>
    public virtual decimal? BasketTo { get; protected set; }

    /// <summary>Bu dilimde uygulanan sabit kargo ücreti (vergi/harç hariç).</summary>
    public virtual decimal Amount { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Verilen sepet tutarı bu dilime düşüyor mu.</summary>
    public virtual bool Covers(decimal basketAmount)
    {
        if (basketAmount < BasketFrom)
        {
            return false;
        }

        return BasketTo is not { } upper || basketAmount < upper;
    }

    /// <summary>İki dilim kesişiyor mu — çakışan barem sessiz yanlış fiyat demektir, ekleme anında engellenir.</summary>
    public virtual bool Overlaps(MarketplaceShipmentConditionalRate other)
    {
        var thisEnd = BasketTo ?? decimal.MaxValue;
        var otherEnd = other.BasketTo ?? decimal.MaxValue;

        return BasketFrom < otherEnd && other.BasketFrom < thisEnd;
    }

    public override string ToString()
    {
        return $"{BasketFrom:N2}–{(BasketTo is { } t ? t.ToString("N2") : "∞")} → {Amount:N2}";
    }

    #endregion
}
