using Integration.TradeXpress.Bullions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// <see cref="BullionLegCalculator"/> saf motor testleri — ERPPRO <c>Cari.AcceptTransaction</c> TAKOZ bloğu davranışı.
/// Beklenen değerler koddan değil ELLE (bağımsız) hesaplanmıştır. İşaret konvansiyonu: + = alacak, − = borç;
/// sonuç bacakları yön işaretlidir, poster ek işaret uygulamaz.
/// </summary>
public class BullionLegCalculatorTests
{
    /// <summary>Varsayılanları nötr (0 / birim kur 1) girdi fabrikası — her test yalnız ilgilendiği alanı kurar.</summary>
    private static BullionLegInput Input(
        ProcessDirectionType direction = ProcessDirectionType.Inbound,
        bool isReport = true,
        decimal amount = 0m,
        decimal assayAmount = 0m,
        decimal goldFactor = 0m,
        decimal silverFactor = 0m,
        decimal platinumFactor = 0m,
        decimal palladiumFactor = 0m,
        MetalDisposition? silverMode = null,
        MetalDisposition? platinumMode = null,
        MetalDisposition? palladiumMode = null,
        decimal goldLaborRate = 0m,
        decimal silverLaborRate = 0m,
        decimal platinumLaborRate = 0m,
        decimal palladiumLaborRate = 0m,
        decimal goldRate = 0m,
        decimal silverRate = 0m,
        decimal platinumRate = 0m,
        decimal palladiumRate = 0m,
        decimal payUnitRate = 1m,
        decimal goldLaborUnitRate = 1m,
        decimal silverLaborUnitRate = 1m,
        decimal platinumLaborUnitRate = 1m,
        decimal palladiumLaborUnitRate = 1m)
    {
        return new BullionLegInput(
            direction, isReport, amount, assayAmount,
            goldFactor, silverFactor, platinumFactor, palladiumFactor,
            silverMode, platinumMode, palladiumMode,
            goldLaborRate, silverLaborRate, platinumLaborRate, palladiumLaborRate,
            goldRate, silverRate, platinumRate, palladiumRate,
            payUnitRate,
            goldLaborUnitRate, silverLaborUnitRate, platinumLaborUnitRate, palladiumLaborUnitRate);
    }

    // ── RAPORSUZ (ham TAKOZ pseudo-bacağı) ────────────────────────────────────

    [Fact]
    public void Unreported_inbound_produces_raw_gram_pseudo_leg_only()
    {
        // 1000g raporsuz giriş → UnreportedTotal = +1000 HAM gram. Metal/işçilik bacakları 0.
        var r = BullionLegCalculator.ComputeBullion(Input(
            isReport: false, amount: 1000m,
            goldFactor: 0.916m, silverFactor: 0.10m));   // milyemler girilmiş olsa bile YOK sayılır

        r.UnreportedTotal.ShouldBe(1000m);
        r.GoldTotal.ShouldBe(0m);
        r.SilverTotal.ShouldBe(0m);
        r.PlatinumTotal.ShouldBe(0m);
        r.PalladiumTotal.ShouldBe(0m);
        r.LaborTotal.ShouldBe(0m);
        r.GrossLabor.ShouldBe(0m);

        // Ground-truth: ×0.6 (Carpan) yalnız GÖSTERİM/konsolidasyondadır — motor ham gram üretir.
        r.UnreportedTotal.ShouldNotBe(1000m * BullionConsts.DefaultCarpan);
    }

    [Fact]
    public void Unreported_inbound_includes_assay_sample_in_quantity()
    {
        // Girişte çeşni numunesi (2g) cari alacağına DAHİL → 1000 + 2 = 1002.
        var r = BullionLegCalculator.ComputeBullion(Input(
            isReport: false, amount: 1000m, assayAmount: 2m));

        r.UnreportedTotal.ShouldBe(1002m);
    }

    [Fact]
    public void Unreported_outbound_is_negative_and_excludes_assay()
    {
        // Çıkışta numune dükkânda kalır → çeşni EKLENMEZ: −1000 (−1002 değil).
        var r = BullionLegCalculator.ComputeBullion(Input(
            direction: ProcessDirectionType.Outbound,
            isReport: false, amount: 1000m, assayAmount: 2m));

        r.UnreportedTotal.ShouldBe(-1000m);
    }

    // ── Yön eşlemesi (çift enum = giriş, tek = çıkış) ─────────────────────────

    [Theory]
    [InlineData(ProcessDirectionType.Inbound,  100)]
    [InlineData(ProcessDirectionType.Credit,   100)]
    [InlineData(ProcessDirectionType.Buy,      100)]
    [InlineData(ProcessDirectionType.Outbound, -100)]
    [InlineData(ProcessDirectionType.Debit,    -100)]
    [InlineData(ProcessDirectionType.Sell,     -100)]
    public void Direction_parity_maps_even_to_inflow_odd_to_outflow(ProcessDirectionType direction, decimal expected)
    {
        var r = BullionLegCalculator.ComputeBullion(Input(
            direction: direction, isReport: false, amount: 100m));

        r.UnreportedTotal.ShouldBe(expected);
    }

    // ── RAPORLU: altın bacağı ─────────────────────────────────────────────────

    [Fact]
    public void Reported_inbound_gold_leg_is_quantity_times_millesimal()
    {
        // 1000g × 0.916 milyem → +916 HAS. Raporsuz bacak 0.
        var r = BullionLegCalculator.ComputeBullion(Input(
            amount: 1000m, goldFactor: 0.916m));

        r.GoldTotal.ShouldBe(916m);
        r.UnreportedTotal.ShouldBe(0m);
        r.SilverTotal.ShouldBe(0m);
        r.LaborTotal.ShouldBe(0m);
    }

    [Fact]
    public void Reported_inbound_assay_sample_enters_gold_leg()
    {
        // qty = 1000 + 2 = 1002; 1002 × 0.9 = 901.8 HAS.
        var r = BullionLegCalculator.ComputeBullion(Input(
            amount: 1000m, assayAmount: 2m, goldFactor: 0.9m));

        r.GoldTotal.ShouldBe(901.8m);
    }

    // ── Yan metal dağıtımları (SilverMode) ────────────────────────────────────

    [Fact]
    public void Silver_deliver_produces_own_silver_leg()
    {
        // Madeni Ver: gümüş kendi biriminde → Silver = 1000 × 0.10 = +100; altına karışmaz.
        var r = BullionLegCalculator.ComputeBullion(Input(
            amount: 1000m, goldFactor: 0.9m, silverFactor: 0.10m,
            silverMode: MetalDisposition.Deliver));

        r.GoldTotal.ShouldBe(900m);
        r.SilverTotal.ShouldBe(100m);
    }

    [Fact]
    public void Silver_mode_null_defaults_to_deliver()
    {
        // Mode girilmemişse (null) varsayılan Madeni Ver'dir.
        var r = BullionLegCalculator.ComputeBullion(Input(
            amount: 1000m, silverFactor: 0.10m, silverMode: null));

        r.SilverTotal.ShouldBe(100m);
    }

    [Fact]
    public void Silver_convert_to_gold_folds_into_gold_leg_via_rates()
    {
        // Altına Çevir: 100g gümüş × 50 (AG kuru) ÷ 4000 (HAS kuru) = 1.25 HAS → altına eklenir.
        var r = BullionLegCalculator.ComputeBullion(Input(
            amount: 1000m, goldFactor: 0.9m, silverFactor: 0.10m,
            silverMode: MetalDisposition.ConvertToGold,
            goldRate: 4000m, silverRate: 50m));

        r.GoldTotal.ShouldBe(901.25m);
        r.SilverTotal.ShouldBe(0m);
    }

    [Fact]
    public void Silver_deduct_from_labor_reduces_net_labor()
    {
        // İşçilikten Düş: brüt işçilik = 20/1000 × 900 = 18; düşülen = 100g × 0.1 = 10; net = 8.
        // Girişte işçilik cariyi BORÇLANDIRIR → LaborTotal = −8. Gümüş bacağı oluşmaz.
        var r = BullionLegCalculator.ComputeBullion(Input(
            amount: 1000m, goldFactor: 0.9m, silverFactor: 0.10m,
            silverMode: MetalDisposition.DeductFromLabor,
            goldLaborRate: 20m, silverRate: 0.1m));

        r.GrossLabor.ShouldBe(18m);
        r.LaborTotal.ShouldBe(-8m);
        r.SilverTotal.ShouldBe(0m);
        r.GoldTotal.ShouldBe(900m);
    }

    [Fact]
    public void Silver_keep_does_not_touch_any_leg()
    {
        // Madeni Bırak: bakiyeye hiç yansımaz.
        var r = BullionLegCalculator.ComputeBullion(Input(
            amount: 1000m, goldFactor: 0.9m, silverFactor: 0.10m,
            silverMode: MetalDisposition.Keep, silverRate: 50m, goldRate: 4000m));

        r.GoldTotal.ShouldBe(900m);
        r.SilverTotal.ShouldBe(0m);
        r.LaborTotal.ShouldBe(0m);
    }

    // ── İşçilik: işaret + birim dönüşümü (PayUnitRate) ────────────────────────

    [Fact]
    public void Labor_inbound_debits_customer_negative_sign()
    {
        // 1000g × 1.0 has, 40/1000 işçilik, tahsil birimi = fiyat birimi (kur 1) → brüt 40, LaborTotal = −40.
        var r = BullionLegCalculator.ComputeBullion(Input(
            amount: 1000m, goldFactor: 1m, goldLaborRate: 40m));

        r.GrossLabor.ShouldBe(40m);
        r.LaborTotal.ShouldBe(-40m);
    }

    [Fact]
    public void Labor_converts_to_pay_unit_via_rate_ratio()
    {
        // İşçilik TRY olarak girilmiş (birim kuru 1) ama HAS olarak tahsil (PayUnitRate = 4000 TRY):
        // 40/1000 × 1000 × 1 ÷ 4000 = 0.01 HAS → LaborTotal = −0.01 (girişte borç).
        var r = BullionLegCalculator.ComputeBullion(Input(
            amount: 1000m, goldFactor: 1m,
            goldLaborRate: 40m, goldLaborUnitRate: 1m, payUnitRate: 4000m));

        r.GrossLabor.ShouldBe(0.01m);
        r.LaborTotal.ShouldBe(-0.01m);
    }

    [Fact]
    public void Labor_accrues_from_all_four_metals()
    {
        // ERPPROV3'ten FARK: PT/PD işçiliği de dahil (4 metal).
        // has: au 900, ag 50, pt 30, pd 20; hepsi 10/1000 → 9 + 0.5 + 0.3 + 0.2 = 10.
        var r = BullionLegCalculator.ComputeBullion(Input(
            amount: 1000m,
            goldFactor: 0.9m, silverFactor: 0.05m, platinumFactor: 0.03m, palladiumFactor: 0.02m,
            goldLaborRate: 10m, silverLaborRate: 10m, platinumLaborRate: 10m, palladiumLaborRate: 10m));

        r.GrossLabor.ShouldBe(10m);
        r.LaborTotal.ShouldBe(-10m);
        // Deliver (varsayılan) → Pt/Pd kendi bacaklarında.
        r.PlatinumTotal.ShouldBe(30m);
        r.PalladiumTotal.ShouldBe(20m);
    }

    [Fact]
    public void Labor_zero_pay_unit_rate_is_division_safe()
    {
        // Sıfır-güvenli bölme: PayUnitRate = 0 → işçilik 0'a düşer, exception atılmaz.
        var r = BullionLegCalculator.ComputeBullion(Input(
            amount: 1000m, goldFactor: 1m, goldLaborRate: 40m, payUnitRate: 0m));

        r.GrossLabor.ShouldBe(0m);
        r.LaborTotal.ShouldBe(0m);
    }

    // ── Ekstra metaller (Pt/Pd) yalnız raporluda ──────────────────────────────

    [Fact]
    public void Platinum_palladium_legs_appear_only_when_reported()
    {
        var input = Input(
            isReport: false, amount: 1000m,
            platinumFactor: 0.03m, palladiumFactor: 0.02m);

        // Raporsuz: Pt/Pd milyemleri yok sayılır, her şey pseudo TAKOZ bacağında.
        var unreported = BullionLegCalculator.ComputeBullion(input);
        unreported.PlatinumTotal.ShouldBe(0m);
        unreported.PalladiumTotal.ShouldBe(0m);
        unreported.UnreportedTotal.ShouldBe(1000m);

        // Raporlu: kendi bacaklarına düşer.
        var reported = BullionLegCalculator.ComputeBullion(input with { IsReport = true });
        reported.PlatinumTotal.ShouldBe(30m);
        reported.PalladiumTotal.ShouldBe(20m);
        reported.UnreportedTotal.ShouldBe(0m);
    }

    [Fact]
    public void Platinum_convert_to_gold_uses_platinum_rate()
    {
        // Pt Altına Çevir: 30g × 2000 ÷ 4000 = 15 HAS altına eklenir; Pt bacağı 0.
        var r = BullionLegCalculator.ComputeBullion(Input(
            amount: 1000m, goldFactor: 0.9m, platinumFactor: 0.03m,
            platinumMode: MetalDisposition.ConvertToGold,
            goldRate: 4000m, platinumRate: 2000m));

        r.GoldTotal.ShouldBe(915m);
        r.PlatinumTotal.ShouldBe(0m);
    }

    // ── ÇIKIŞ: işaretler ters ─────────────────────────────────────────────────

    [Fact]
    public void Reported_outbound_inverts_metal_and_labor_signs()
    {
        // Çıkışta metal bacakları (−), işçilik cariyi ALACAKLANDIRIR (+18).
        var r = BullionLegCalculator.ComputeBullion(Input(
            direction: ProcessDirectionType.Outbound,
            amount: 1000m, goldFactor: 0.9m, silverFactor: 0.10m,
            silverMode: MetalDisposition.Deliver, goldLaborRate: 20m));

        r.GoldTotal.ShouldBe(-900m);
        r.SilverTotal.ShouldBe(-100m);
        r.LaborTotal.ShouldBe(18m);
        r.GrossLabor.ShouldBe(18m);   // brüt işçilik işaretsiz raporlanır
    }

    [Fact]
    public void Same_input_inbound_vs_outbound_legs_are_exact_mirrors()
    {
        // Yön-işaret simetrisi (çeşni 0 iken): tüm bacaklar birebir ters işaretli.
        var inbound = Input(
            amount: 500m, assayAmount: 0m,
            goldFactor: 0.916m, silverFactor: 0.06m, platinumFactor: 0.02m, palladiumFactor: 0.01m,
            silverMode: MetalDisposition.Deliver,
            platinumMode: MetalDisposition.ConvertToGold,
            palladiumMode: MetalDisposition.DeductFromLabor,
            goldLaborRate: 15m, silverLaborRate: 5m, platinumLaborRate: 8m, palladiumLaborRate: 8m,
            goldRate: 4000m, silverRate: 50m, platinumRate: 2000m, palladiumRate: 1500m);

        var rIn  = BullionLegCalculator.ComputeBullion(inbound);
        var rOut = BullionLegCalculator.ComputeBullion(inbound with { Direction = ProcessDirectionType.Outbound });

        rOut.GoldTotal.ShouldBe(-rIn.GoldTotal);
        rOut.SilverTotal.ShouldBe(-rIn.SilverTotal);
        rOut.PlatinumTotal.ShouldBe(-rIn.PlatinumTotal);
        rOut.PalladiumTotal.ShouldBe(-rIn.PalladiumTotal);
        rOut.LaborTotal.ShouldBe(-rIn.LaborTotal);
        rOut.UnreportedTotal.ShouldBe(-rIn.UnreportedTotal);   // ikisi de 0 (raporlu)
        rOut.GrossLabor.ShouldBe(rIn.GrossLabor);              // brüt işçilik yön işaretsiz
    }

    [Fact]
    public void Assay_sample_breaks_mirror_symmetry()
    {
        // Çeşni > 0 iken simetri BOZULUR: giriş 1002 × 0.9, çıkış 1000 × 0.9.
        var input = Input(amount: 1000m, assayAmount: 2m, goldFactor: 0.9m);

        var rIn  = BullionLegCalculator.ComputeBullion(input);
        var rOut = BullionLegCalculator.ComputeBullion(input with { Direction = ProcessDirectionType.Outbound });

        rIn.GoldTotal.ShouldBe(901.8m);
        rOut.GoldTotal.ShouldBe(-900m);
    }
}
