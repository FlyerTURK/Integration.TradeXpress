using System.Linq;
using Shouldly;
using Xunit;
using static Integration.TradeXpress.Vouchers.Balance.BalanceTestLine;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// 12 <see cref="IVoucherLineBalancePoster"/>'ın karakterizasyon testleri — her ProcessType
/// için işaret doğruluğu (+ giriş / − çıkış), peşin (WithCash) muafiyeti dalları ve leg
/// dağıtımı. İşaret konvansiyonu: + = ALACAK, − = BORÇ (<see cref="BalanceEffect"/>).
/// </summary>
public class BalancePosterTests
{
    // ── NAKİT (Cash) — pay leg'i, peşin muaf ─────────────────────────────────

    [Fact]
    public void Cash_with_cash_payment_has_no_effect()
    {
        var line = Create(ProcessType.Cash, paymentType: ProcessPaymentType.WithCash,
                          payUnitId: TryUnit, payTotal: 100m);

        new CashBalancePoster().Post(line).ShouldBeEmpty();
    }

    [Fact]
    public void Cash_without_pay_unit_has_no_effect()
    {
        var line = Create(ProcessType.Cash, payUnitId: null, payTotal: 100m);

        new CashBalancePoster().Post(line).ShouldBeEmpty();
    }

    [Fact]
    public void Cash_inbound_credits_and_outbound_debits_pay_total()
    {
        var inbound  = Create(ProcessType.Cash, ProcessDirectionType.Inbound,
                              payUnitId: TryUnit, payTotal: 150m);
        var outbound = Create(ProcessType.Cash, ProcessDirectionType.Outbound,
                              payUnitId: TryUnit, payTotal: 150m);

        new CashBalancePoster().Post(inbound).ShouldBe(new[] { new BalanceEffect(TryUnit, 150m) });
        new CashBalancePoster().Post(outbound).ShouldBe(new[] { new BalanceEffect(TryUnit, -150m) });
    }

    // ── HİZMET (Service) — Nakit ile aynı işaret + peşin muafiyeti ───────────

    [Fact]
    public void Service_with_cash_payment_has_no_effect()
    {
        var line = Create(ProcessType.Service, paymentType: ProcessPaymentType.WithCash,
                          payUnitId: TryUnit, payTotal: 100m);

        new ServiceBalancePoster().Post(line).ShouldBeEmpty();
    }

    [Fact]
    public void Service_inbound_credits_and_outbound_debits_pay_total()
    {
        var inbound  = Create(ProcessType.Service, ProcessDirectionType.Inbound,
                              payUnitId: UsdUnit, payTotal: 75m);
        var outbound = Create(ProcessType.Service, ProcessDirectionType.Outbound,
                              payUnitId: UsdUnit, payTotal: 75m);

        new ServiceBalancePoster().Post(inbound).ShouldBe(new[] { new BalanceEffect(UsdUnit, 75m) });
        new ServiceBalancePoster().Post(outbound).ShouldBe(new[] { new BalanceEffect(UsdUnit, -75m) });
    }

    // ── DEKONT (DebitNote) — peşin muafiyeti BİLİNÇLİ YOK ────────────────────

    [Fact]
    public void Debit_note_posts_even_with_cash_payment()
    {
        // Dekont DAİMA bakiyeye yazar (legacy BORC=999 tek leg paritesi).
        var line = Create(ProcessType.DebitNote, paymentType: ProcessPaymentType.WithCash,
                          payUnitId: TryUnit, payTotal: 200m);

        new DebitNoteBalancePoster().Post(line).ShouldBe(new[] { new BalanceEffect(TryUnit, 200m) });
    }

    [Fact]
    public void Debit_note_outbound_debits_and_missing_unit_skips()
    {
        var outbound = Create(ProcessType.DebitNote, ProcessDirectionType.Outbound,
                              payUnitId: TryUnit, payTotal: 200m);
        var noUnit   = Create(ProcessType.DebitNote, payUnitId: null, payTotal: 200m);

        new DebitNoteBalancePoster().Post(outbound).ShouldBe(new[] { new BalanceEffect(TryUnit, -200m) });
        new DebitNoteBalancePoster().Post(noUnit).ShouldBeEmpty();
    }

    // ── VİRMAN (Transfer) — peşin muafiyeti yok, ikiz satırlar ayrı postlar ──

    [Fact]
    public void Transfer_posts_even_with_cash_payment_and_signs_by_direction()
    {
        var source = Create(ProcessType.Transfer, ProcessDirectionType.Outbound,
                            paymentType: ProcessPaymentType.WithCash,
                            payUnitId: TryUnit, payTotal: 300m);
        var twin   = Create(ProcessType.Transfer, ProcessDirectionType.Inbound,
                            payUnitId: TryUnit, payTotal: 300m);

        new TransferBalancePoster().Post(source).ShouldBe(new[] { new BalanceEffect(TryUnit, -300m) });
        new TransferBalancePoster().Post(twin).ShouldBe(new[] { new BalanceEffect(TryUnit, 300m) });
    }

    // ── TAŞ (Stone) / MÜCEVHER (Jewelry) — parasal tek leg ───────────────────

    [Fact]
    public void Stone_with_cash_payment_or_zero_total_has_no_effect()
    {
        var cash = Create(ProcessType.Stone, paymentType: ProcessPaymentType.WithCash,
                          payUnitId: TryUnit, payTotal: 100m);
        var zero = Create(ProcessType.Stone, payUnitId: TryUnit, payTotal: 0m);

        new StoneBalancePoster().Post(cash).ShouldBeEmpty();
        new StoneBalancePoster().Post(zero).ShouldBeEmpty();
    }

    [Fact]
    public void Stone_inbound_credits_and_outbound_debits_pay_total()
    {
        var inbound  = Create(ProcessType.Stone, ProcessDirectionType.Inbound,
                              payUnitId: UsdUnit, payTotal: 5000m);
        var outbound = Create(ProcessType.Stone, ProcessDirectionType.Outbound,
                              payUnitId: UsdUnit, payTotal: 5000m);

        new StoneBalancePoster().Post(inbound).ShouldBe(new[] { new BalanceEffect(UsdUnit, 5000m) });
        new StoneBalancePoster().Post(outbound).ShouldBe(new[] { new BalanceEffect(UsdUnit, -5000m) });
    }

    [Fact]
    public void Jewelry_mirrors_stone_behavior()
    {
        var cash     = Create(ProcessType.Jewelry, paymentType: ProcessPaymentType.WithCash,
                              payUnitId: TryUnit, payTotal: 100m);
        var inbound  = Create(ProcessType.Jewelry, ProcessDirectionType.Inbound,
                              payUnitId: TryUnit, payTotal: 900m);
        var outbound = Create(ProcessType.Jewelry, ProcessDirectionType.Outbound,
                              payUnitId: TryUnit, payTotal: 900m);

        new JewelryBalancePoster().Post(cash).ShouldBeEmpty();
        new JewelryBalancePoster().Post(inbound).ShouldBe(new[] { new BalanceEffect(TryUnit, 900m) });
        new JewelryBalancePoster().Post(outbound).ShouldBe(new[] { new BalanceEffect(TryUnit, -900m) });
    }

    [Fact]
    public void Good_mirrors_jewelry_behavior()
    {
        var cash     = Create(ProcessType.Good, paymentType: ProcessPaymentType.WithCash,
                              payUnitId: TryUnit, payTotal: 100m);
        var inbound  = Create(ProcessType.Good, ProcessDirectionType.Inbound,
                              payUnitId: TryUnit, payTotal: 750m);
        var outbound = Create(ProcessType.Good, ProcessDirectionType.Outbound,
                              payUnitId: TryUnit, payTotal: 750m);

        new GoodBalancePoster().Post(cash).ShouldBeEmpty();
        new GoodBalancePoster().Post(inbound).ShouldBe(new[] { new BalanceEffect(TryUnit, 750m) });
        new GoodBalancePoster().Post(outbound).ShouldBe(new[] { new BalanceEffect(TryUnit, -750m) });
    }

    // ── ÇEVİR (Convert) — iki leg: ana − / karşı + (Alacak yönünde) ──────────

    [Fact]
    public void Convert_credit_debits_main_and_credits_pay_leg()
    {
        // Alacak (Credit): kaynak birimden ÇIKAR (−100 USD), hedef birime GİRER (+4500 TRY).
        var line = Create(ProcessType.Convert, ProcessDirectionType.Credit,
                          mainUnitId: UsdUnit, total: 100m,
                          payUnitId: TryUnit, payTotal: 4500m);

        new ConvertBalancePoster().Post(line).ShouldBe(new[]
        {
            new BalanceEffect(UsdUnit, -100m),
            new BalanceEffect(TryUnit, 4500m),
        });
    }

    [Fact]
    public void Convert_debit_reverses_both_legs()
    {
        var line = Create(ProcessType.Convert, ProcessDirectionType.Debit,
                          mainUnitId: UsdUnit, total: 100m,
                          payUnitId: TryUnit, payTotal: 4500m);

        new ConvertBalancePoster().Post(line).ShouldBe(new[]
        {
            new BalanceEffect(UsdUnit, 100m),
            new BalanceEffect(TryUnit, -4500m),
        });
    }

    [Fact]
    public void Convert_skips_zero_or_unitless_legs()
    {
        var zeroMain = Create(ProcessType.Convert, ProcessDirectionType.Credit,
                              mainUnitId: UsdUnit, total: 0m,
                              payUnitId: TryUnit, payTotal: 4500m);

        new ConvertBalancePoster().Post(zeroMain).ShouldBe(new[] { new BalanceEffect(TryUnit, 4500m) });
    }

    // ── VADELİ (Future) — Çevir'in TERSİ işaret ──────────────────────────────

    [Fact]
    public void Future_buy_credits_main_and_debits_pay_leg()
    {
        // Alış (Buy): mal GİRER (+100 USD), bedel BORÇLANIR (−4500 TRY) — Çevir'in tersi.
        var line = Create(ProcessType.Future, ProcessDirectionType.Buy,
                          mainUnitId: UsdUnit, total: 100m,
                          payUnitId: TryUnit, payTotal: 4500m);

        new FutureBalancePoster().Post(line).ShouldBe(new[]
        {
            new BalanceEffect(UsdUnit, 100m),
            new BalanceEffect(TryUnit, -4500m),
        });
    }

    [Fact]
    public void Future_sell_reverses_both_legs()
    {
        var line = Create(ProcessType.Future, ProcessDirectionType.Sell,
                          mainUnitId: UsdUnit, total: 100m,
                          payUnitId: TryUnit, payTotal: 4500m);

        new FutureBalancePoster().Post(line).ShouldBe(new[]
        {
            new BalanceEffect(UsdUnit, -100m),
            new BalanceEffect(TryUnit, 4500m),
        });
    }

    // ── MADEN (Metal) — ödeme tipine göre 0/1/2 leg ──────────────────────────

    [Fact]
    public void Metal_with_cash_payment_has_no_effect()
    {
        var line = Create(ProcessType.Metal, paymentType: ProcessPaymentType.WithCash,
                          mainUnitId: HasUnit, total: 100m, payUnitId: TryUnit, payTotal: 20m);

        new MetalBalancePoster().Post(line).ShouldBeEmpty();
    }

    [Fact]
    public void Metal_reservation_has_no_effect_in_both_directions()
    {
        // Rezervasyon = taahhüt sayacı — bakiyeye YANSIMAZ (Peşin gibi bakiye-dışı),
        // ana Has + işçilik alanları dolu olsa bile hiçbir leg yazılmaz.
        var inbound  = Create(ProcessType.Metal, ProcessDirectionType.Inbound,
                              paymentType: ProcessPaymentType.Reservation,
                              mainUnitId: HasUnit, total: 100m, payUnitId: LaborUnit, payTotal: 20m);
        var outbound = Create(ProcessType.Metal, ProcessDirectionType.Outbound,
                              paymentType: ProcessPaymentType.Reservation,
                              mainUnitId: HasUnit, total: 100m, payUnitId: LaborUnit, payTotal: 20m);

        new MetalBalancePoster().Post(inbound).ShouldBeEmpty();
        new MetalBalancePoster().Post(outbound).ShouldBeEmpty();
    }

    [Fact]
    public void Metal_with_currency_posts_only_price_leg()
    {
        // Bedelli: maden leg'i YOK (işçilik Factor'a yedirilmiş) → yalnız bedel.
        var line = Create(ProcessType.Metal, paymentType: ProcessPaymentType.WithCurrency,
                          mainUnitId: HasUnit, total: 100m, payUnitId: TryUnit, payTotal: 4500m);

        new MetalBalancePoster().Post(line).ShouldBe(new[] { new BalanceEffect(TryUnit, 4500m) });
    }

    [Fact]
    public void Metal_normal_posts_main_and_labor_legs_same_sign()
    {
        var inbound  = Create(ProcessType.Metal, ProcessDirectionType.Inbound,
                              mainUnitId: HasUnit, total: 100m, payUnitId: LaborUnit, payTotal: 20m);
        var outbound = Create(ProcessType.Metal, ProcessDirectionType.Outbound,
                              mainUnitId: HasUnit, total: 100m, payUnitId: LaborUnit, payTotal: 20m);

        new MetalBalancePoster().Post(inbound).ShouldBe(new[]
        {
            new BalanceEffect(HasUnit, 100m),
            new BalanceEffect(LaborUnit, 20m),
        });
        new MetalBalancePoster().Post(outbound).ShouldBe(new[]
        {
            new BalanceEffect(HasUnit, -100m),
            new BalanceEffect(LaborUnit, -20m),
        });
    }

    // ── HURDA (Scrap) — Metal'den fark: Normal'de işçilik leg'i YOK ──────────

    [Fact]
    public void Scrap_with_cash_payment_has_no_effect()
    {
        var line = Create(ProcessType.Scrap, paymentType: ProcessPaymentType.WithCash,
                          mainUnitId: HasUnit, total: 100m, payUnitId: TryUnit, payTotal: 4500m);

        new ScrapBalancePoster().Post(line).ShouldBeEmpty();
    }

    [Fact]
    public void Scrap_with_currency_posts_only_pay_leg()
    {
        var line = Create(ProcessType.Scrap, paymentType: ProcessPaymentType.WithCurrency,
                          mainUnitId: HasUnit, total: 100m, payUnitId: TryUnit, payTotal: 4500m);

        new ScrapBalancePoster().Post(line).ShouldBe(new[] { new BalanceEffect(TryUnit, 4500m) });
    }

    [Fact]
    public void Scrap_normal_posts_main_leg_only_even_if_pay_leg_is_filled()
    {
        // Metal'den fark: Normal'de yalnız ana Has leg'i — işçilik cariye YAZILMAZ.
        var line = Create(ProcessType.Scrap, ProcessDirectionType.Outbound,
                          mainUnitId: HasUnit, total: 91.6m, payUnitId: LaborUnit, payTotal: 20m);

        new ScrapBalancePoster().Post(line).ShouldBe(new[] { new BalanceEffect(HasUnit, -91.6m) });
    }

    // ── ÇEŞNİ (Assay) — daima çıkış, para leg'i yok ──────────────────────────

    [Fact]
    public void Assay_posts_negative_metal_legs_from_millesimals()
    {
        // 10g × 0.9 = 9 HAS ve 10g × 0.05 = 0.5 GUM — ikisi de BORÇ (−).
        var line = Create(ProcessType.Assay,
                          amount: 10m, factor: 0.9m, mainUnitId: HasUnit,
                          silverFactor: 0.05m, silverUnitId: GumUnit);

        new AssayBalancePoster().Post(line).ShouldBe(new[]
        {
            new BalanceEffect(HasUnit, -9m),
            new BalanceEffect(GumUnit, -0.5m),
        });
    }

    [Fact]
    public void Assay_skips_zero_factor_legs()
    {
        var line = Create(ProcessType.Assay,
                          amount: 10m, factor: 0.9m, mainUnitId: HasUnit,
                          silverFactor: 0m, silverUnitId: GumUnit);

        new AssayBalancePoster().Post(line).ShouldBe(new[] { new BalanceEffect(HasUnit, -9m) });
    }

    [Fact]
    public void Assay_sign_is_direction_independent_always_outflow()
    {
        // Poster Direction okumaz — yön daima ÇIKIŞ kabul edilir (legacy CESNI).
        var inbound = Create(ProcessType.Assay, ProcessDirectionType.Inbound,
                             amount: 10m, factor: 0.9m, mainUnitId: HasUnit);

        new AssayBalancePoster().Post(inbound).ShouldBe(new[] { new BalanceEffect(HasUnit, -9m) });
    }

    // ── TAKOZ (Bullion) — leg'ler motor işaretli, poster ek işaret UYGULAMAZ ─

    [Fact]
    public void Bullion_unreported_posts_single_pseudo_leg_without_extra_sign()
    {
        var inbound  = Create(ProcessType.Bullion, ProcessDirectionType.Inbound,
                              amount: 1000m, mainUnitId: HasUnit, isReport: false);
        var outbound = Create(ProcessType.Bullion, ProcessDirectionType.Outbound,
                              amount: 1000m, mainUnitId: HasUnit, isReport: false);

        new BullionBalancePoster().Post(inbound).ShouldBe(new[] { new BalanceEffect(HasUnit, 1000m) });
        new BullionBalancePoster().Post(outbound).ShouldBe(new[] { new BalanceEffect(HasUnit, -1000m) });
    }

    [Fact]
    public void Bullion_reported_distributes_metal_and_labor_legs_to_units()
    {
        // 1000g × 0.916 = +916 HAS; gümüş 0.10 (varsayılan Madeni Ver) = +100 GUM;
        // işçilik = 20/1000 × 916 × 1 ÷ 1 = 18.32 → girişte BORÇ = −18.32 (işçilik birimi).
        var line = Create(ProcessType.Bullion,
                          amount: 1000m, factor: 0.916m, mainUnitId: HasUnit,
                          silverFactor: 0.10m, silverUnitId: GumUnit,
                          payFactor: 20m,             // altın işçilik fiyatı (mevcut alan)
                          payUnitId: LaborUnit, payUnitRate: 1m,
                          goldLaborUnitRate: 1m,
                          isReport: true);

        new BullionBalancePoster().Post(line).ShouldBe(new[]
        {
            new BalanceEffect(HasUnit, 916m),
            new BalanceEffect(GumUnit, 100m),
            new BalanceEffect(LaborUnit, -18.32m),
        });
    }

    [Fact]
    public void Bullion_missing_side_unit_drops_that_leg()
    {
        // Gümüş leg'i hesaplansa da SilverUnitId yoksa postlanamaz — sessizce düşer.
        var line = Create(ProcessType.Bullion,
                          amount: 1000m, factor: 0.9m, mainUnitId: HasUnit,
                          silverFactor: 0.10m, silverUnitId: null,
                          isReport: true);

        new BullionBalancePoster().Post(line).ShouldBe(new[] { new BalanceEffect(HasUnit, 900m) });
    }

    // ── Poster ↔ ProcessType eşlemesi (kayıt doğruluğu) ──────────────────────

    [Fact]
    public void Posters_declare_their_own_process_type()
    {
        new CashBalancePoster().ProcessType.ShouldBe(ProcessType.Cash);
        new ServiceBalancePoster().ProcessType.ShouldBe(ProcessType.Service);
        new ConvertBalancePoster().ProcessType.ShouldBe(ProcessType.Convert);
        new FutureBalancePoster().ProcessType.ShouldBe(ProcessType.Future);
        new MetalBalancePoster().ProcessType.ShouldBe(ProcessType.Metal);
        new ScrapBalancePoster().ProcessType.ShouldBe(ProcessType.Scrap);
        new StoneBalancePoster().ProcessType.ShouldBe(ProcessType.Stone);
        new JewelryBalancePoster().ProcessType.ShouldBe(ProcessType.Jewelry);
        new GoodBalancePoster().ProcessType.ShouldBe(ProcessType.Good);
        new TransferBalancePoster().ProcessType.ShouldBe(ProcessType.Transfer);
        new AssayBalancePoster().ProcessType.ShouldBe(ProcessType.Assay);
        new BullionBalancePoster().ProcessType.ShouldBe(ProcessType.Bullion);
        new DebitNoteBalancePoster().ProcessType.ShouldBe(ProcessType.DebitNote);
    }
}
