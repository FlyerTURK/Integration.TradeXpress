using System;
using Integration.TradeXpress.Currencies;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Currencies;

public class CurrencyPriceCalculatorTests
{
    // ── DeriveDirect ──────────────────────────────────────────────────────────

    [Fact]
    public void Direct_override_replaces_garbage_feed_no_guard()
    {
        // Feed PLD 13000/18000 (geniş makas) → kullanıcı 16000/17000 ile ezer.
        var p = CurrencyPriceCalculator.DeriveDirect(
            marketBuy: 13000m, marketSell: 18000m,
            marginOnBuy: MarginSetting.Fixed(16000m),
            marginOnSell: MarginSetting.Fixed(17000m));

        p.Buy.ShouldBe(16000m);
        p.Sell.ShouldBe(17000m);
        p.GuardFired.ShouldBeFalse();
    }

    [Fact]
    public void Direct_passthrough_keeps_feed()
    {
        var p = CurrencyPriceCalculator.DeriveDirect(
            40m, 41m, MarginSetting.Passthrough, MarginSetting.Passthrough);

        p.Buy.ShouldBe(40m);
        p.Sell.ShouldBe(41m);
        p.GuardFired.ShouldBeFalse();
    }

    // ── Guard (felaket: alış > satış → swap) ──────────────────────────────────

    [Fact]
    public void Guard_swaps_when_buy_exceeds_sell_and_flags()
    {
        // Misconfig: alış 18000 > satış 13000 → takas → 13000/18000, fired.
        var p = CurrencyPriceCalculator.DeriveDirect(
            0m, 0m, MarginSetting.Fixed(18000m), MarginSetting.Fixed(13000m));

        p.Buy.ShouldBe(13000m);
        p.Sell.ShouldBe(18000m);
        p.GuardFired.ShouldBeTrue();
    }

    [Fact]
    public void Guard_allows_equal_buy_sell()
    {
        var p = CurrencyPriceCalculator.Guard(100m, 100m);
        p.Buy.ShouldBe(100m);
        p.Sell.ShouldBe(100m);
        p.GuardFired.ShouldBeFalse();
        p.NonPositive.ShouldBeFalse();
    }

    [Fact]
    public void Guard_flags_negative_price_as_non_positive()
    {
        // Amount -200 ile alış negatife düşer → düzeltilmez, NonPositive işaretlenir.
        var p = CurrencyPriceCalculator.DeriveDirect(
            100m, 100m,
            marginOnBuy: new MarginSetting(MarginType.Amount, -200m),  // -100
            marginOnSell: MarginSetting.Passthrough);                  // 100
        p.Buy.ShouldBe(-100m);
        p.NonPositive.ShouldBeTrue();
    }

    [Fact]
    public void Guard_flags_zero_price_as_non_positive()
        => CurrencyPriceCalculator.Guard(0m, 10m).NonPositive.ShouldBeTrue();

    [Fact]
    public void Guard_positive_prices_are_valid()
        => CurrencyPriceCalculator.Guard(16000m, 17000m).NonPositive.ShouldBeFalse();

    // ── DeriveFollowing (iki aşamalı) ─────────────────────────────────────────

    [Fact]
    public void Following_applies_followingMargin_then_buy_sell_margins()
    {
        // parent 40/41 → FollowingMargin +%1 → 40.4/41.41 → buy/sell margin no-op.
        var p = CurrencyPriceCalculator.DeriveFollowing(
            parentBuy: 40m, parentSell: 41m,
            followingMargin: new MarginSetting(MarginType.Percent, 1m),
            marginOnBuy: MarginSetting.Passthrough,
            marginOnSell: MarginSetting.Passthrough);

        p.Buy.ShouldBe(40.4m);
        p.Sell.ShouldBe(41.41m);
        p.GuardFired.ShouldBeFalse();
    }

    [Fact]
    public void Following_then_buy_sell_spread_added()
    {
        // parent 40/40 (no spread), follow no-op, sonra alış -0.5 / satış +0.5 → 39.5/40.5.
        var p = CurrencyPriceCalculator.DeriveFollowing(
            40m, 40m,
            MarginSetting.Passthrough,
            new MarginSetting(MarginType.Amount, -0.5m),
            new MarginSetting(MarginType.Amount, 0.5m));

        p.Buy.ShouldBe(39.5m);
        p.Sell.ShouldBe(40.5m);
    }

    [Fact]
    public void Following_two_stage_order_is_followingMargin_then_buy_sell()
    {
        // Non-commutative pin: following Percent +%10 ÖNCE, sonra Amount +5.
        // 100×1.1=110, +5=115. (Ters sıra olsaydı: 100+5=105, ×1.1=115.5.)
        var p = CurrencyPriceCalculator.DeriveFollowing(
            100m, 100m,
            followingMargin: new MarginSetting(MarginType.Percent, 10m),
            marginOnBuy: new MarginSetting(MarginType.Amount, 5m),
            marginOnSell: new MarginSetting(MarginType.Amount, 5m));

        p.Buy.ShouldBe(115m);
        p.Sell.ShouldBe(115m);
    }

    [Fact]
    public void Following_guard_fires_when_margins_invert()
    {
        // Follow yolunda da guard çalışır: alış 50 > satış 45 → takas + flag.
        var p = CurrencyPriceCalculator.DeriveFollowing(
            40m, 41m, MarginSetting.Passthrough,
            marginOnBuy: MarginSetting.Fixed(50m),
            marginOnSell: MarginSetting.Fixed(45m));

        p.Buy.ShouldBe(45m);
        p.Sell.ShouldBe(50m);
        p.GuardFired.ShouldBeTrue();
    }

    [Fact]
    public void Following_finalPrice_following_margin_flattens_parent_spread()
    {
        // FollowingMargin=FinalPrice(100) → parent alış/satış İKİSİ de 100 (spread ölür),
        // sonra alış/satış margin spread ekler.
        var p = CurrencyPriceCalculator.DeriveFollowing(
            40m, 41m,
            followingMargin: MarginSetting.Fixed(100m),
            marginOnBuy: new MarginSetting(MarginType.Amount, -1m),
            marginOnSell: new MarginSetting(MarginType.Amount, 1m));

        p.Buy.ShouldBe(99m);
        p.Sell.ShouldBe(101m);
    }

    // ── ReBase (base currency = 1, spread olsa bile) ──────────────────────────

    [Fact]
    public void ReBase_base_currency_becomes_one_even_with_spread()
    {
        // ABD şirketi: base = USD, GERÇEK spread'li (40/41). Base kendine göre → (1,1).
        var usdPivot = new CurrencyPrice(40m, 41m, false);
        var usd = CurrencyPriceCalculator.ReBase(usdPivot, baseInPivot: usdPivot);
        usd.Buy.ShouldBe(1m);
        usd.Sell.ShouldBe(1m);
    }

    [Fact]
    public void ReBase_other_currency_relative_to_base()
    {
        // TRY pivot (1,1), base USD (40,40) → (0.025, 0.025).
        var trY = CurrencyPriceCalculator.ReBase(
            new CurrencyPrice(1m, 1m, false), new CurrencyPrice(40m, 40m, false));
        trY.Buy.ShouldBe(0.025m);
        trY.Sell.ShouldBe(0.025m);
    }

    [Fact]
    public void ReBase_preserves_guard_flag()
        => CurrencyPriceCalculator.ReBase(
            new CurrencyPrice(10m, 20m, true), new CurrencyPrice(2m, 2m, false)).GuardFired.ShouldBeTrue();

    [Fact]
    public void ReBase_rejects_nonpositive_base()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => CurrencyPriceCalculator.ReBase(
                new CurrencyPrice(1m, 1m, false), new CurrencyPrice(0m, 1m, false)));

    // ── ApplyLayer (kademe: host efektif → tenant efektif) ────────────────────

    [Fact]
    public void Cascade_host_then_tenant_amount_margins()
    {
        // Ham 40/50 → host Amount −5/+5 → 35/55 → tenant Amount −5/+5 → 30/60.
        var host = CurrencyPriceCalculator.DeriveDirect(
            40m, 50m,
            new MarginSetting(MarginType.Amount, -5m),
            new MarginSetting(MarginType.Amount, 5m));
        host.Buy.ShouldBe(35m);
        host.Sell.ShouldBe(55m);

        var tenant = CurrencyPriceCalculator.ApplyLayer(
            host,
            new MarginSetting(MarginType.Amount, -5m),
            new MarginSetting(MarginType.Amount, 5m));
        tenant.Buy.ShouldBe(30m);
        tenant.Sell.ShouldBe(60m);
    }

    [Fact]
    public void ApplyLayer_passthrough_is_identity()
    {
        var p = new CurrencyPrice(36m, 56m, false);
        var layered = CurrencyPriceCalculator.ApplyLayer(p, MarginSetting.Passthrough, MarginSetting.Passthrough);
        layered.Buy.ShouldBe(36m);
        layered.Sell.ShouldBe(56m);
    }

    [Fact]
    public void ApplyLayer_multiply_is_frame_free_ratio()
    {
        // Oran katmanı: 36/56 × 1.10 → 39.6 / 61.6 (para birimi boyutu yok).
        var p = new CurrencyPrice(36m, 56m, false);
        var layered = CurrencyPriceCalculator.ApplyLayer(
            p, new MarginSetting(MarginType.Multiply, 1.10m), new MarginSetting(MarginType.Multiply, 1.10m));
        layered.Buy.ShouldBe(39.6m);
        layered.Sell.ShouldBe(61.6m);
    }

    [Fact]
    public void ApplyLayer_accumulates_guard_flag_from_lower_layer()
    {
        // Alt katman guard tetiklemişse üst katman temiz olsa bile flag korunur.
        var lower = new CurrencyPrice(35m, 55m, GuardFired: true);
        var layered = CurrencyPriceCalculator.ApplyLayer(lower, MarginSetting.Passthrough, MarginSetting.Passthrough);
        layered.GuardFired.ShouldBeTrue();
    }

    // ── Cascade (scope zinciri: host → tenant) ────────────────────────────────

    [Fact]
    public void Cascade_host_then_tenant_chain()
    {
        // Ham 40/50 → [host −5/+5, tenant −5/+5] → 30/60 (senaryo).
        var raw = new CurrencyPrice(40m, 50m, false);
        var eff = CurrencyPriceCalculator.Cascade(raw,
            (new MarginSetting(MarginType.Amount, -5m), new MarginSetting(MarginType.Amount, 5m)),
            (new MarginSetting(MarginType.Amount, -5m), new MarginSetting(MarginType.Amount, 5m)));
        eff.Buy.ShouldBe(30m);
        eff.Sell.ShouldBe(60m);
    }

    [Fact]
    public void Cascade_host_only_chain_is_single_layer()
    {
        // Host viewer: tek katman (host marjı). Ham 40/50 → host −5/+5 → 35/55.
        var raw = new CurrencyPrice(40m, 50m, false);
        var eff = CurrencyPriceCalculator.Cascade(raw,
            (new MarginSetting(MarginType.Amount, -5m), new MarginSetting(MarginType.Amount, 5m)));
        eff.Buy.ShouldBe(35m);
        eff.Sell.ShouldBe(55m);
    }

    [Fact]
    public void Cascade_no_layers_returns_raw()
    {
        var raw = new CurrencyPrice(36m, 56m, false);
        CurrencyPriceCalculator.Cascade(raw).ShouldBe(raw);
    }

    // ── Cross (parite bid/ask çapraz kuru) ────────────────────────────────────

    [Fact]
    public void Cross_against_pivot_is_direct()
    {
        // quote = TRY (1/1) → USDTRY = USD'nin TRY fiyatı (doğrudan).
        var usd = new CurrencyPrice(36m, 40m, false);
        var trY = new CurrencyPrice(1m, 1m, false);
        var p = CurrencyPriceCalculator.Cross(usd, trY);
        p.Buy.ShouldBe(36m);
        p.Sell.ShouldBe(40m);
    }

    [Fact]
    public void Cross_legs_invert_for_real_pair_spread()
    {
        // EUR(39/43) / USD(36/40): bid=39/40, ask=43/36 (bacaklar çaprazda ters).
        var eur = new CurrencyPrice(39m, 43m, false);
        var usd = new CurrencyPrice(36m, 40m, false);
        var p = CurrencyPriceCalculator.Cross(eur, usd);
        p.Buy.ShouldBe(39m / 40m);   // 0.975
        p.Sell.ShouldBe(43m / 36m);  // ~1.194
        (p.Buy < p.Sell).ShouldBeTrue();
    }

    [Fact]
    public void Cross_rejects_nonpositive_quote()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => CurrencyPriceCalculator.Cross(new CurrencyPrice(1m, 1m, false), new CurrencyPrice(0m, 1m, false)));
}
