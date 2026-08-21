using System;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// <see cref="VoucherLineCalculator"/> saf motor testleri — karakterizasyon: beklenen değerler
/// mevcut üretim davranışından elle (bağımsız) hesaplanmıştır. Kur/parite bağımlılıkları
/// delege stub'larıyla verilir; motor infra'sızdır.
/// Sabit senaryo kurları (TL alış): USD=45 · TRY=1 · EUR=40.
/// </summary>
public class VoucherLineCalculatorTests
{
    private static readonly Guid UsdId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid EurId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UnknownId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    /// <summary>Sabit alış kurları — bilinmeyen birim 0 döner (motor sözleşmesi).</summary>
    private static decimal BuyRate(Guid unitId)
    {
        if (unitId == UsdId) { return 45m; }
        if (unitId == TryId) { return 1m; }
        if (unitId == EurId) { return 40m; }
        return 0m;
    }

    /// <summary>Her çift için sabit Main (base) dönen parite stub'ı.</summary>
    private static Func<Guid, Guid, Guid?> ParityMain(Guid main)
    {
        return (_, _) => main;
    }

    /// <summary>Parite kaydı yok senaryosu.</summary>
    private static readonly Func<Guid, Guid, Guid?> NoParity = (_, _) => null;

    /// <summary>Varsayılanları nötr girdi fabrikası — her test yalnız ilgilendiği alanı kurar.</summary>
    private static VoucherLineCalcInput Input(
        ProcessType processType = ProcessType.Cash,
        ProcessDirectionType direction = ProcessDirectionType.Inbound,
        ProcessPaymentType? paymentType = ProcessPaymentType.Normal,
        Guid? mainUnitId = null,
        Guid? payUnitId = null,
        decimal amount = 0m,
        decimal factor = 0m,
        decimal total = 0m,
        decimal payFactor = 0m,
        decimal payTotal = 0m,
        decimal marketPrice = 0m,
        EditedField editedField = EditedField.None)
    {
        return new VoucherLineCalcInput(
            processType, direction, paymentType,
            mainUnitId, payUnitId,
            amount, factor, total,
            payFactor, payTotal, marketPrice, editedField);
    }

    // ── Parite yönü: çarp / böl ───────────────────────────────────────────────

    [Fact]
    public void Main_is_parity_base_multiplies_amount_by_market_price()
    {
        // Ana birim (USD) parite kaydının Main'i → ÇARP: 100 × 45 = 4500.
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UsdId, payUnitId: TryId, amount: 100m),
            BuyRate, ParityMain(UsdId));

        r.MarketPrice.ShouldBe(45m);      // doğal parite = buy(USD)/buy(TRY)
        r.PayFactor.ShouldBe(45m);
        r.PayTotal.ShouldBe(4500m);
        r.Amount.ShouldBe(100m);
        r.Factor.ShouldBe(1m);            // nakitte çarpan daima 1
        r.Total.ShouldBe(100m);           // ana leg toplamı = miktar
        r.Profit.ShouldBe(0m);            // piyasa fiyatından işlem → kâr 0
    }

    [Fact]
    public void Pay_is_parity_base_divides_amount_by_market_price()
    {
        // Ana birim TRY, kaydın Main'i USD → BÖL: 4500 ÷ 45 = 100.
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: TryId, payUnitId: UsdId, amount: 4500m),
            BuyRate, ParityMain(UsdId));

        r.MarketPrice.ShouldBe(45m);      // doğal parite yön ne olursa olsun 45.59-tarzı görünür
        r.PayFactor.ShouldBe(45m);
        r.PayTotal.ShouldBe(100m);
        r.Profit.ShouldBe(0m);
    }

    [Fact]
    public void Market_price_is_call_order_independent()
    {
        // USD/TRY hangi leg'de olursa olsun görünen doğal parite aynıdır (45).
        var mainUsd = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UsdId, payUnitId: TryId, amount: 1m), BuyRate, ParityMain(UsdId));
        var mainTry = VoucherLineCalculator.Calculate(
            Input(mainUnitId: TryId, payUnitId: UsdId, amount: 1m), BuyRate, ParityMain(UsdId));

        mainUsd.MarketPrice.ShouldBe(mainTry.MarketPrice);
    }

    // ── PayTotal geri-hesabı (EditedField.PayTotal) ──────────────────────────

    [Fact]
    public void Edited_pay_total_back_computes_factor_in_multiply_direction()
    {
        // Tutar 4600 girildi, miktar 100 → Fiyat = 4600 ÷ 100 = 46 (çarpım yönü).
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UsdId, payUnitId: TryId, amount: 100m,
                  payTotal: 4600m, editedField: EditedField.PayTotal),
            BuyRate, ParityMain(UsdId));

        r.PayFactor.ShouldBe(46m);
        r.PayTotal.ShouldBe(4600m);
        // Kâr = Satış − Maliyet = 4600×1 − 100×45 = 100 TL.
        r.Profit.ShouldBe(100m);
    }

    [Fact]
    public void Edited_pay_total_back_computes_factor_in_divide_direction()
    {
        // Böl yönü: Fiyat = Miktar ÷ Tutar = 4500 ÷ 90 = 50.
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: TryId, payUnitId: UsdId, amount: 4500m,
                  payTotal: 90m, editedField: EditedField.PayTotal),
            BuyRate, ParityMain(UsdId));

        r.PayFactor.ShouldBe(50m);
        r.PayTotal.ShouldBe(90m);
    }

    [Fact]
    public void Edited_pay_total_zero_in_divide_direction_falls_back_to_market_price()
    {
        // Böl yönünde Tutar 0 → sıfıra bölme yok, Fiyat piyasa değerine döner.
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: TryId, payUnitId: UsdId, amount: 4500m,
                  payTotal: 0m, editedField: EditedField.PayTotal),
            BuyRate, ParityMain(UsdId));

        r.PayFactor.ShouldBe(45m);
        r.PayTotal.ShouldBe(0m);
    }

    [Fact]
    public void Edited_pay_total_with_empty_amount_derives_amount_multiply()
    {
        // Miktar boş → market fiyatla türet: 4500 ÷ 45 = 100 (çarpım yönü).
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UsdId, payUnitId: TryId, amount: 0m,
                  payTotal: 4500m, editedField: EditedField.PayTotal),
            BuyRate, ParityMain(UsdId));

        r.PayFactor.ShouldBe(45m);
        r.Amount.ShouldBe(100m);
        r.Total.ShouldBe(100m);           // türetilen miktar ana leg'e de yansır
        r.PayTotal.ShouldBe(4500m);
    }

    [Fact]
    public void Edited_pay_total_with_empty_amount_derives_amount_divide()
    {
        // Böl yönü: Miktar = Tutar × Fiyat = 100 × 45 = 4500.
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: TryId, payUnitId: UsdId, amount: 0m,
                  payTotal: 100m, editedField: EditedField.PayTotal),
            BuyRate, ParityMain(UsdId));

        r.Amount.ShouldBe(4500m);
        r.PayTotal.ShouldBe(100m);
    }

    [Fact]
    public void Edited_pay_total_with_no_price_information_keeps_amount_zero()
    {
        // Fiyat da piyasa da yok (bilinmeyen birimler) → Miktar türetilemez, 0 kalır.
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UnknownId, payUnitId: EurId, amount: 0m,
                  payTotal: 500m, editedField: EditedField.PayTotal),
            BuyRate, NoParity);

        r.MarketPrice.ShouldBe(0m);       // buy(Unknown)=0 → doğal parite 0
        r.PayFactor.ShouldBe(0m);
        r.Amount.ShouldBe(0m);
    }

    // ── Aynı birim (same-unit kilidi) ─────────────────────────────────────────

    [Fact]
    public void Same_unit_locks_factor_to_one_and_pay_total_to_amount()
    {
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UsdId, payUnitId: UsdId, amount: 250m),
            BuyRate, ParityMain(UsdId));

        r.MarketPrice.ShouldBe(1m);
        r.PayFactor.ShouldBe(1m);
        r.PayTotal.ShouldBe(250m);
        r.PayFactorReadOnly.ShouldBeTrue();   // UI kilidi motor kararı
        r.PayTotalReadOnly.ShouldBeTrue();
        r.Profit.ShouldBe(0m);
    }

    // ── Fiyatı koruma vs. yeniden yükleme ────────────────────────────────────

    [Fact]
    public void Edited_amount_keeps_user_pay_factor()
    {
        // Miktar değişti → kullanıcının fiyatı (44) korunur, market'e (45) EZİLMEZ.
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UsdId, payUnitId: TryId, amount: 100m,
                  payFactor: 44m, editedField: EditedField.Amount),
            BuyRate, ParityMain(UsdId));

        r.PayFactor.ShouldBe(44m);
        r.PayTotal.ShouldBe(4400m);
    }

    [Fact]
    public void Edited_amount_with_zero_pay_factor_falls_back_to_market_price()
    {
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UsdId, payUnitId: TryId, amount: 100m,
                  payFactor: 0m, editedField: EditedField.Amount),
            BuyRate, ParityMain(UsdId));

        r.PayFactor.ShouldBe(45m);
        r.PayTotal.ShouldBe(4500m);
    }

    [Fact]
    public void Structural_change_reloads_market_price_over_user_factor()
    {
        // Yapısal değişim (PayUnit) → parite yeniden yüklenir; kullanıcı fiyatı (46) ezilir.
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UsdId, payUnitId: TryId, amount: 100m,
                  payFactor: 46m, editedField: EditedField.PayUnit),
            BuyRate, ParityMain(UsdId));

        r.PayFactor.ShouldBe(45m);
        r.PayTotal.ShouldBe(4500m);
    }

    [Fact]
    public void Structural_change_with_unknown_rates_keeps_incoming_factor()
    {
        // Farklı ama kuru bilinmeyen birimler → marketPrice 0 → gelen fiyat korunur.
        var otherUnknown = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UnknownId, payUnitId: otherUnknown, amount: 100m,
                  payFactor: 46m, editedField: EditedField.Commodity),
            BuyRate, NoParity);

        r.MarketPrice.ShouldBe(0m);
        r.PayFactor.ShouldBe(46m);
        r.PayTotal.ShouldBe(4600m);
        r.Profit.ShouldBe(0m);            // kur bilinmeyince kâr hesaplanamaz → 0
    }

    // ── Parite kaydı yok / sıfır kur fallback'leri ───────────────────────────

    [Fact]
    public void Missing_parity_record_assumes_straight_ratio_in_multiply_direction()
    {
        // Kayıt yok → PayFactor = buy(Main)/buy(Pay) = 45/40 = 1.125, çarpım yönü.
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UsdId, payUnitId: EurId, amount: 100m),
            BuyRate, NoParity);

        r.MarketPrice.ShouldBe(1.125m);
        r.PayFactor.ShouldBe(1.125m);
        r.PayTotal.ShouldBe(112.5m);
        r.Profit.ShouldBe(0m);            // 112.5×40 − 100×45 = 0
    }

    [Fact]
    public void Zero_quote_rate_yields_zero_market_price_without_throwing()
    {
        // Karşı birimin kuru 0 → sıfıra bölme YOK, doğal parite 0'a düşer.
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UsdId, payUnitId: UnknownId, amount: 100m),
            BuyRate, ParityMain(UsdId));

        r.MarketPrice.ShouldBe(0m);
        r.PayFactor.ShouldBe(0m);
        r.PayTotal.ShouldBe(0m);
    }

    [Fact]
    public void Missing_main_unit_yields_zero_market_price()
    {
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: null, payUnitId: TryId, amount: 100m, payFactor: 5m,
                  editedField: EditedField.Amount),
            BuyRate, ParityMain(UsdId));

        r.MarketPrice.ShouldBe(0m);
        r.PayFactor.ShouldBe(5m);         // gelen fiyat korunur
        r.PayTotal.ShouldBe(500m);        // birim yokken çarpım yönü varsayılır
        r.Profit.ShouldBe(0m);            // buy(Main) yok → kâr 0
    }

    // ── Kâr formülü ──────────────────────────────────────────────────────────

    [Fact]
    public void Profit_is_direction_independent()
    {
        // Kâr = Satış − Maliyet (alış kurlarıyla TL); Giriş/Çıkış işareti kârı DEĞİŞTİRMEZ.
        var input = Input(mainUnitId: UsdId, payUnitId: TryId, amount: 100m,
                          payTotal: 4600m, editedField: EditedField.PayTotal);

        var inbound  = VoucherLineCalculator.Calculate(input, BuyRate, ParityMain(UsdId));
        var outbound = VoucherLineCalculator.Calculate(
            input with { Direction = ProcessDirectionType.Outbound }, BuyRate, ParityMain(UsdId));

        inbound.Profit.ShouldBe(100m);
        outbound.Profit.ShouldBe(100m);
    }

    [Fact]
    public void Profit_is_zero_when_either_rate_is_unknown()
    {
        var r = VoucherLineCalculator.Calculate(
            Input(mainUnitId: UnknownId, payUnitId: TryId, amount: 100m,
                  payFactor: 5m, editedField: EditedField.Amount),
            BuyRate, NoParity);

        r.Profit.ShouldBe(0m);
    }

    // ── Yön (isInflow) ve pay-combo kaynağı ──────────────────────────────────

    [Theory]
    [InlineData(ProcessDirectionType.Inbound,  true)]
    [InlineData(ProcessDirectionType.Outbound, false)]
    [InlineData(ProcessDirectionType.Credit,   true)]
    [InlineData(ProcessDirectionType.Debit,    false)]
    [InlineData(ProcessDirectionType.Buy,      true)]
    [InlineData(ProcessDirectionType.Sell,     false)]
    public void Is_inflow_maps_even_directions_to_true(ProcessDirectionType direction, bool expected)
    {
        var r = VoucherLineCalculator.Calculate(
            Input(direction: direction, mainUnitId: UsdId, payUnitId: TryId, amount: 1m),
            BuyRate, ParityMain(UsdId));

        r.IsInflow.ShouldBe(expected);
    }

    [Theory]
    [InlineData(ProcessType.Cash,    ProcessPaymentType.WithCash, PayCommoditySource.CashInstruments)]
    [InlineData(ProcessType.Cash,    ProcessPaymentType.Normal,   PayCommoditySource.Units)]
    [InlineData(ProcessType.Convert, ProcessPaymentType.WithCash, PayCommoditySource.Units)]   // peşin olsa da Units'e ZORLANIR
    [InlineData(ProcessType.Scrap,   ProcessPaymentType.WithCash, PayCommoditySource.Units)]
    [InlineData(ProcessType.Bullion, ProcessPaymentType.WithCash, PayCommoditySource.Units)]
    [InlineData(ProcessType.Stone,   ProcessPaymentType.WithCash, PayCommoditySource.CashInstruments)]   // default dal ödeme tipine bakar
    public void Pay_commodity_source_follows_process_and_payment_type(
        ProcessType processType, ProcessPaymentType paymentType, PayCommoditySource expected)
    {
        var r = VoucherLineCalculator.Calculate(
            Input(processType: processType, paymentType: paymentType,
                  mainUnitId: UsdId, payUnitId: TryId, amount: 1m),
            BuyRate, ParityMain(UsdId));

        r.PayCommoditySource.ShouldBe(expected);
    }

    // ── Passthrough dalları (Bullion / Assay / bilinmeyen tür) ───────────────

    [Theory]
    [InlineData(ProcessType.Bullion)]
    [InlineData(ProcessType.Assay)]
    [InlineData(ProcessType.Stone)]    // poster'sız default dal da passthrough
    public void Passthrough_types_echo_inputs_without_computation(ProcessType processType)
    {
        var r = VoucherLineCalculator.Calculate(
            Input(processType: processType, mainUnitId: UsdId, payUnitId: TryId,
                  amount: 5m, factor: 0.9m, total: 4.5m,
                  payFactor: 2m, payTotal: 10m, marketPrice: 3m),
            BuyRate, ParityMain(UsdId));

        r.Amount.ShouldBe(5m);
        r.Factor.ShouldBe(0.9m);          // Cash'ten fark: Factor 1'e normalize EDİLMEZ
        r.Total.ShouldBe(4.5m);
        r.PayFactor.ShouldBe(2m);
        r.PayTotal.ShouldBe(10m);
        r.MarketPrice.ShouldBe(3m);
        r.Profit.ShouldBe(0m);
        r.PayFactorReadOnly.ShouldBeFalse();
        r.PayTotalReadOnly.ShouldBeFalse();
    }

    [Theory]
    [InlineData(ProcessType.Convert)]
    [InlineData(ProcessType.Future)]
    [InlineData(ProcessType.Scrap)]
    [InlineData(ProcessType.Metal)]
    public void Unit_priced_types_share_cash_parity_math(ProcessType processType)
    {
        // Çevir/Vadeli/Hurda/Maden pay leg'i Nakit ile aynı parite matematiğini kullanır.
        var r = VoucherLineCalculator.Calculate(
            Input(processType: processType, mainUnitId: UsdId, payUnitId: TryId, amount: 100m),
            BuyRate, ParityMain(UsdId));

        r.PayFactor.ShouldBe(45m);
        r.PayTotal.ShouldBe(4500m);
    }

    // ── Fail-fast ────────────────────────────────────────────────────────────

    [Fact]
    public void Null_delegates_throw_argument_null()
    {
        var input = Input(mainUnitId: UsdId, payUnitId: TryId, amount: 1m);

        Should.Throw<ArgumentNullException>(() =>
            VoucherLineCalculator.Calculate(input, null!, ParityMain(UsdId)));
        Should.Throw<ArgumentNullException>(() =>
            VoucherLineCalculator.Calculate(input, BuyRate, null!));
    }
}
