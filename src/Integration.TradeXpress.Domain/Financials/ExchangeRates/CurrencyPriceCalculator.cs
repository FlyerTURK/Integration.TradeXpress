namespace Integration.TradeXpress.Financials.ExchangeRates;

/// <summary>
/// Nihai alış/satış fiyatı + guard sinyalleri.
/// <see cref="GuardFired"/>: ters-makas (alış>satış) düzeltildi (takas).
/// <see cref="NonPositive"/>: alış veya satış ≤ 0 — geçersiz fiyat (düzeltilmez, işaretlenir;
/// edit-time'da blok/uyarı, runtime'da görünür kalır).
/// </summary>
public sealed record CurrencyPrice(decimal Buy, decimal Sell, bool GuardFired, bool NonPositive = false);

/// <summary>
/// Fiyatlama matematiğinin TEK saf (DB/feed/entity bağımsız) yeri:
/// margin assembly + felaket guard'ı + base-currency re-basing. Tamamen test edilebilir.
///
/// <para>Hiçbir süreç entity seviyesinde değil — entity yalnız config (MarginSetting) tutar,
/// hesap burada yapılır.</para>
/// </summary>
public static class CurrencyPriceCalculator
{
    /// <summary>
    /// Doğrudan-feed birim: ham market alış/satışa kendi düzeltme margin'lerini uygular,
    /// ardından guard.
    /// </summary>
    public static CurrencyPrice DeriveDirect(
        decimal marketBuy, decimal marketSell,
        MarginSetting marginOnBuy, MarginSetting marginOnSell)
    {
        ArgumentNullException.ThrowIfNull(marginOnBuy);
        ArgumentNullException.ThrowIfNull(marginOnSell);

        var buy  = marginOnBuy.Apply(marketBuy);
        var sell = marginOnSell.Apply(marketSell);
        return Guard(buy, sell);
    }

    /// <summary>
    /// Takip eden birim: önce parent'ın final alış/satışına <paramref name="followingMargin"/>
    /// (her iki yana aynı kural), sonra alış/satış margin'leri, ardından guard.
    /// </summary>
    public static CurrencyPrice DeriveFollowing(
        decimal parentBuy, decimal parentSell,
        MarginSetting followingMargin,
        MarginSetting marginOnBuy, MarginSetting marginOnSell)
    {
        ArgumentNullException.ThrowIfNull(followingMargin);
        ArgumentNullException.ThrowIfNull(marginOnBuy);
        ArgumentNullException.ThrowIfNull(marginOnSell);

        // ① önce takip edilen birime margin
        var followedBuy  = followingMargin.Apply(parentBuy);
        var followedSell = followingMargin.Apply(parentSell);

        // ② sonra alış/satış margin'leri
        var buy  = marginOnBuy.Apply(followedBuy);
        var sell = marginOnSell.Apply(followedSell);

        return Guard(buy, sell);
    }

    /// <summary>
    /// Felaket guard'ı: invariant <c>alış ≤ satış</c>. Eşit serbest; alış > satış
    /// (misconfig / ters feed) durumunda alış↔satış <b>takas</b> edilir ve
    /// <see cref="CurrencyPrice.GuardFired"/> işaretlenir (sessizce fabrike etme).
    /// Clamp YOK — kullanıcının feed-üstü override'ı (PLD 16000/17000) korunur.
    /// </summary>
    public static CurrencyPrice Guard(decimal buy, decimal sell)
    {
        var swapped = buy > sell;
        if (swapped)
            (buy, sell) = (sell, buy);

        // Non-pozitif (negatif/sıfır) fiyat geçersizdir — düzeltilmez, işaretlenir.
        var nonPositive = buy <= 0m || sell <= 0m;

        return new CurrencyPrice(buy, sell, GuardFired: swapped, NonPositive: nonPositive);
    }

    /// <summary>
    /// Mevcut bir fiyatın ÜSTÜNE bir margin katmanı uygular (kademe/cascade primitifi).
    /// Kademede her seviyenin efektifi bir alttakinin ham'ıdır: host efektifi → tenant
    /// onun üstüne kendi marjını uygular. Marj görülen fiyatın biriminde (pivot TRY).
    /// Guard her katmanda yeniden uygulanır; <see cref="CurrencyPrice.GuardFired"/> birikir (OR).
    /// </summary>
    public static CurrencyPrice ApplyLayer(CurrencyPrice price, MarginSetting onBuy, MarginSetting onSell)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(onBuy);
        ArgumentNullException.ThrowIfNull(onSell);

        var layered = Guard(onBuy.Apply(price.Buy), onSell.Apply(price.Sell));
        return layered with { GuardFired = layered.GuardFired || price.GuardFired };
    }

    /// <summary>
    /// Ham fiyata bir <b>scope zincirini</b> sırayla uygular (kademe): host → tenant → branch…
    /// Her katman bir alttakinin efektifinin üstüne biner (<see cref="ApplyLayer"/>). Katman yoksa
    /// ham döner. Host viewer için zincir tek katmandır (host marjı); tenant için host + tenant.
    /// Marjlar görülen fiyatın biriminde (pivot TRY); para-birimi çevirimi yok.
    /// </summary>
    public static CurrencyPrice Cascade(CurrencyPrice raw, params (MarginSetting OnBuy, MarginSetting OnSell)[] layers)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var price = raw;
        foreach (var (onBuy, onSell) in layers)
            price = ApplyLayer(price, onBuy, onSell);
        return price;
    }

    /// <summary>
    /// Pivot (feed) cinsinden bir fiyatı, aktif şirketin <b>base currency</b>'sine çevirir
    /// (re-base): her bacak, base biriminin <b>karşılık gelen bacağına</b> bölünür
    /// (<c>Buy/base.Buy</c>, <c>Sell/base.Sell</c>). Böylece base birim kendisine göre
    /// re-base edilince <b>her spread'de (1,1)</b> çıkar (identity) — ABD şirketinde
    /// USD alış ve satış = 1, spread olsa bile. Diğer birimler base'e göre relatif.
    ///
    /// <para>Bu imza aynı zamanda ileriki bid/ask çapraz-çevrimin (alışı base-satışla
    /// böl vb.) doğal dikiş yeridir; v1'de aynı-bacak bölme yeterli.</para>
    /// </summary>
    /// <summary>
    /// Parite (forex) bid/ask <b>çapraz</b> kuru: <c>1 base = X quote</c>. İki birimin pivot
    /// (TRY) efektif fiyatından, bacaklar TERS dönerek: <c>bid = base.bid / quote.ask</c>,
    /// <c>ask = base.ask / quote.bid</c>. Böylece iki geniş makas birleşince doğru (geniş) parite
    /// makası çıkar. (DEĞERLEME'nin <see cref="ReBase"/>'inden farklıdır: ReBase per-leg böler,
    /// base'i 1/1 yapar — muhasebe; Cross gerçek çapraz kuru verir — parite panosu.)
    /// </summary>
    public static CurrencyPrice Cross(CurrencyPrice baseInPivot, CurrencyPrice quoteInPivot)
    {
        ArgumentNullException.ThrowIfNull(baseInPivot);
        ArgumentNullException.ThrowIfNull(quoteInPivot);
        if (quoteInPivot.Buy <= 0m || quoteInPivot.Sell <= 0m)
        {
            // Payda sıfır/negatif olamaz — çapraz kur tanımsız (BusinessException: error-code + lokalize).
            throw new BusinessException("TradeXpress:ExchangeRate:QuotePriceMustBePositive");
        }

        var bid = baseInPivot.Buy / quoteInPivot.Sell;
        var ask = baseInPivot.Sell / quoteInPivot.Buy;
        return Guard(bid, ask);
    }

    public static CurrencyPrice ReBase(CurrencyPrice priceInPivot, CurrencyPrice baseInPivot)
    {
        ArgumentNullException.ThrowIfNull(priceInPivot);
        ArgumentNullException.ThrowIfNull(baseInPivot);
        if (baseInPivot.Buy <= 0m || baseInPivot.Sell <= 0m)
        {
            // Payda sıfır/negatif olamaz — re-base tanımsız (BusinessException: error-code + lokalize).
            throw new BusinessException("TradeXpress:ExchangeRate:BasePriceMustBePositive");
        }

        return new CurrencyPrice(
            priceInPivot.Buy  / baseInPivot.Buy,
            priceInPivot.Sell / baseInPivot.Sell,
            priceInPivot.GuardFired,
            priceInPivot.NonPositive);
    }
}
