using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Piyasa kuru zaman-serisi arşivi (append-only, worker yazar, read-only history).
/// Coarse cadence (ör. 15-dk) — canlı tick'ler burada değil, bellek cache'inde.
///
/// <para>Pivot (feed) cinsinden saklanır; re-basing okuma anında aktif Company base'ine göre
/// yapılır. Audit log değil — bu domain verisi (bkz. ne zaman ne fiyat/margin kullanıldı).</para>
///
/// <para>Takip eden birimde <see cref="MarketPriceOnBuy"/>/<see cref="MarketPriceOnSell"/>
/// = parent fiyatına FollowingMargin uygulanmış değer; <see cref="AppliedMarginOnBuy"/>/Sell
/// o satırı üreten alış/satış margin'lerinin snapshot'ı (ne zaman ne margin görünür).</para>
/// </summary>
public class ExchangeRate : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid CurrencyUnitId { get; protected set; }

    public virtual decimal MarketPriceOnBuy  { get; protected set; }
    public virtual decimal MarketPriceOnSell { get; protected set; }

    /// <summary>Bu satırı üreten alış/satış margin'lerinin snapshot'ı (tarihsel audit).</summary>
    public virtual MarginSetting AppliedMarginOnBuy  { get; protected set; } = MarginSetting.Passthrough;
    public virtual MarginSetting AppliedMarginOnSell { get; protected set; } = MarginSetting.Passthrough;

    public virtual string Source { get; protected set; } = null!;
    public virtual DateTime RateDate { get; protected set; }

    /// <summary>Felaket guard'ı (alış>satış → takas) bu satırda tetiklendi mi.</summary>
    public virtual bool GuardFired { get; protected set; }

    protected ExchangeRate() { }

    public ExchangeRate(
        Guid id,
        Guid currencyUnitId,
        decimal marketPriceOnBuy,
        decimal marketPriceOnSell,
        MarginSetting appliedMarginOnBuy,
        MarginSetting appliedMarginOnSell,
        string source,
        DateTime rateDate,
        bool guardFired = false)
        : base(id)
    {
        CurrencyUnitId      = currencyUnitId;
        // Sıkı invariant: kur fiyatı KESİNLİKLE > 0. Non-pozitif feed/tick asla persist edilmez
        // (worker CurrencyPrice.NonPositive ile eler; bu ctor son savunma hattı).
        MarketPriceOnBuy    = RequirePositive(marketPriceOnBuy,  nameof(marketPriceOnBuy));
        MarketPriceOnSell   = RequirePositive(marketPriceOnSell, nameof(marketPriceOnSell));
        AppliedMarginOnBuy  = appliedMarginOnBuy  ?? MarginSetting.Passthrough;
        AppliedMarginOnSell = appliedMarginOnSell ?? MarginSetting.Passthrough;
        Source              = Check.NotNullOrWhiteSpace(source, nameof(source), CurrencyConsts.RateSourceMaxLength);
        RateDate            = rateDate;
        GuardFired          = guardFired;
    }

    private static decimal RequirePositive(decimal value, string paramName)
    {
        if (value <= 0m)
            throw new ArgumentOutOfRangeException(paramName, value, "Kur fiyatı kesinlikle 0'dan büyük olmalıdır.");
        return value;
    }
}
