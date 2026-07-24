using System;
using System.Collections.Generic;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// <see cref="ProductRecipeCostCalculator"/> saf hesap testi (DB'siz). İki satır türü: fiziki katalog
/// (Metal/Scrap/Future/Jewelry/Stone — kendi gerçek maliyeti) ve <b>Hizmet</b> (türevsel bedel: devralınan taban
/// üstüne yüzde/brütleştir/… ; PİLOT). Satır Maliyeti = satırın KATKISI (fiziki: gerçek maliyet; Hizmet: uygulanan
/// bedel/fee) → net = basit toplam. Değerleme SATIŞ bacağı (2026-07-05), ülke birimine rebase. Kur eksik = MissingRate.
/// </summary>
public class ProductRecipeCostCalculatorTests
{
    private static readonly Guid Has = Guid.NewGuid();
    private static readonly Guid Usd = Guid.NewGuid();
    private static readonly Guid Try = Guid.NewGuid();
    private static readonly Guid Unknown = Guid.NewGuid();

    private readonly ProductRecipeCostCalculator _calculator = new();

    // Doğal birim → "1 birim = X ülke parası" (SATIŞ bacağı). TRY ülke birimi (kendisi 1).
    private static Dictionary<Guid, decimal> Sell() => new() { [Has] = 6000m, [Usd] = 30m, [Try] = 1m };

    private static RecipeLineCostInput MetalLine(
        decimal quantity = 0m, decimal amount = 0m, decimal factor = 1m,
        bool isQuantity = false, decimal stableQuantity = 0m,
        ProcessPaymentType paymentType = ProcessPaymentType.Normal,
        decimal payFactor = 0m, Guid? payUnitId = null, bool laborByQuantity = false)
    {
        return new RecipeLineCostInput(
            RecipeComponentType.CatalogCommodity, ProcessType.Metal,
            quantity, amount, factor,
            isQuantity, stableQuantity,
            PriceByQuantity: false, EntryPrice: 0m,
            NaturalUnitId: Has,
            paymentType, payFactor, payUnitId, laborByQuantity,
            ManualAmount: null, ManualUnitId: null);
    }

    /// <summary>Parasal (Stone) fiziki satır — taban maliyet üretmek için: EntryPrice × 1 @ birim.
    /// <c>PricedLine(1000, Try)</c> → 1000 TRY.</summary>
    private static RecipeLineCostInput PricedLine(decimal entryPrice, Guid unitId)
    {
        return new RecipeLineCostInput(
            RecipeComponentType.CatalogCommodity, ProcessType.Stone,
            Quantity: 0m, Amount: 1m, Factor: 0m,
            IsQuantity: false, StableQuantity: 0m,
            PriceByQuantity: false, EntryPrice: entryPrice,
            NaturalUnitId: unitId,
            ProcessPaymentType.Normal, PayFactor: 0m, PayUnitId: null, LaborByQuantity: false,
            ManualAmount: null, ManualUnitId: null);
    }

    /// <summary>Hizmet (türevsel bedel) satırı — taban modu + işlem + operand (+ SelectedLines ordinal'leri).</summary>
    private static RecipeLineCostInput ServiceLine(
        RecipeDerivedBaseMode baseMode, RecipeDerivedOperation operation, decimal operand,
        IReadOnlyList<int>? sourceOrdinals = null, Guid? payUnitId = null)
    {
        return new RecipeLineCostInput(
            RecipeComponentType.Service, Family: null,
            Quantity: 0m, Amount: 0m, Factor: 0m,
            IsQuantity: false, StableQuantity: 0m,
            PriceByQuantity: false, EntryPrice: 0m,
            NaturalUnitId: null,
            ProcessPaymentType.Normal, PayFactor: 0m, PayUnitId: payUnitId, LaborByQuantity: false,
            ManualAmount: null, ManualUnitId: null,
            DerivedBaseMode: baseMode, DerivedOperation: operation, DerivedOperand: operand,
            DerivedSourceOrdinals: sourceOrdinals);
    }

    // ── fiziki katalog satırları (ComputeLine — değişmedi) ──────────────────────────────────────────

    [Fact]
    public void Metal_gram_leg_without_labor_uses_amount_times_factor_on_sell()
    {
        // Gramlı metal, işçiliksiz: 10g × 0.995 = 9.95 HAS (Total); × 6000 = 59700.00.
        var result = _calculator.Compute(new[] { MetalLine(amount: 10m, factor: 0.995m) }, Sell(), "TRY");

        result.Lines[0].MissingRate.ShouldBeFalse();
        result.Lines[0].Total.ShouldBe(9.95m);
        result.Lines[0].Cost.ShouldBe(59700.00m);
        result.Lines[0].RunningSubtotal.ShouldBe(59700.00m);
        result.Net.ShouldBe(59700.00m);
        result.CurrencyCode.ShouldBe("TRY");
    }

    [Fact]
    public void Metal_quantity_leg_uses_quantity_times_stable_times_factor()
    {
        // Adetli metal (sikke): 2 adet × 5g/adet × 1.0 = 10 HAS; × 6000 = 60000.00.
        var result = _calculator.Compute(
            new[] { MetalLine(quantity: 2m, factor: 1m, isQuantity: true, stableQuantity: 5m) }, Sell(), "TRY");

        result.Lines[0].Total.ShouldBe(10m);
        result.Lines[0].Cost.ShouldBe(60000.00m);
    }

    [Fact]
    public void Normal_payment_adds_labor_leg_to_metal_leg()
    {
        // Normal: metal 10g×1.0 = 10 HAS × 6000 = 60000; işçilik miktar-bazlı 5 TRY/g × 10g = 50 TRY × 1 = 50.
        var result = _calculator.Compute(
            new[] { MetalLine(amount: 10m, factor: 1m, payFactor: 5m, payUnitId: Try) }, Sell(), "TRY");

        result.Lines[0].Total.ShouldBe(10m);
        result.Lines[0].PayTotal.ShouldBe(50m);
        result.Lines[0].Cost.ShouldBe(60050.00m);   // iki bacak toplamı
        result.Net.ShouldBe(60050.00m);
    }

    [Fact]
    public void Normal_payment_labor_by_quantity_multiplies_rate_with_count()
    {
        // Adet-bazlı işçilik: 2 adet × 5g = 10 HAS metal; işçilik 3 USD/ADET × 2 = 6 USD × 30 = 180.
        var result = _calculator.Compute(
            new[]
            {
                MetalLine(
                    quantity: 2m, factor: 1m, isQuantity: true, stableQuantity: 5m,
                    payFactor: 3m, payUnitId: Usd, laborByQuantity: true),
            },
            Sell(), "TRY");

        result.Lines[0].PayTotal.ShouldBe(6m);
        result.Lines[0].Cost.ShouldBe(60180.00m);   // 60000 + 180
    }

    [Fact]
    public void WithCurrency_payment_is_single_leg_total_times_payfactor()
    {
        // BEDELLİ: 10 HAS × 5900 TRY/HAS = 59000 TRY — TEK bacak (metal bacağı AYRICA eklenmez; çift sayım yok).
        var result = _calculator.Compute(
            new[]
            {
                MetalLine(
                    amount: 10m, factor: 1m,
                    paymentType: ProcessPaymentType.WithCurrency, payFactor: 5900m, payUnitId: Try),
            },
            Sell(), "TRY");

        result.Lines[0].Total.ShouldBe(10m);        // görüntü değeri (HAS)
        result.Lines[0].PayTotal.ShouldBe(59000m);
        result.Lines[0].Cost.ShouldBe(59000.00m);   // yalnız bedel bacağı (canlı 60000 DEĞİL)
    }

    [Fact]
    public void Normal_payment_with_labor_but_missing_pay_rate_marks_line_missing()
    {
        // İşçilik bacağının birimi çözülemiyor → satır MissingRate, net'e katılmaz.
        var result = _calculator.Compute(
            new[] { MetalLine(amount: 10m, factor: 1m, payFactor: 5m, payUnitId: Unknown) }, Sell(), "TRY");

        result.Lines[0].MissingRate.ShouldBeTrue();
        result.Lines[0].Cost.ShouldBeNull();
        result.Net.ShouldBe(0m);
        result.AnyMissingRate.ShouldBeTrue();
    }

    [Fact]
    public void Priced_leg_uses_entry_price_times_gram_when_not_price_by_quantity()
    {
        // Taş/Mücevher parasal: 4g × EntryPrice 25 (USD) = 100 USD; × 30 = 3000.00. Pay bacağı yok.
        var line = new RecipeLineCostInput(
            RecipeComponentType.CatalogCommodity, ProcessType.Stone,
            Quantity: 0m, Amount: 4m, Factor: 0m,
            IsQuantity: false, StableQuantity: 0m,
            PriceByQuantity: false, EntryPrice: 25m,
            NaturalUnitId: Usd,
            ProcessPaymentType.Normal, PayFactor: 0m, PayUnitId: null, LaborByQuantity: false,
            ManualAmount: null, ManualUnitId: null);

        var result = _calculator.Compute(new[] { line }, Sell(), "TRY");

        result.Lines[0].Total.ShouldBe(100m);
        result.Lines[0].Cost.ShouldBe(3000.00m);
    }

    [Fact]
    public void Line_with_unresolvable_unit_is_marked_missing_and_excluded_from_net()
    {
        var priced = MetalLine(amount: 10m, factor: 1m);          // 60000
        var missing = PricedLine(entryPrice: 100m, unitId: Unknown);   // birim çözülemez → missing

        var result = _calculator.Compute(new[] { priced, missing }, Sell(), "TRY");

        result.Lines[0].Cost.ShouldBe(60000.00m);
        result.Lines[1].MissingRate.ShouldBeTrue();
        result.Lines[1].Cost.ShouldBeNull();
        result.AnyMissingRate.ShouldBeTrue();
        result.Net.ShouldBe(60000.00m);             // eksik satır net'e katılmaz
    }

    /// <summary>Fiyatı ÇÖZÜLEMEYEN parasal satır (PriceUnknown) 0 maliyetle DEĞİL, MissingRate ile döner —
    /// aksi halde fiyatsız bir Mamül "bedava" sayılıp net maliyeti sessizce eksik bırakırdı (kod-inceleme bulgusu:
    /// Good'un fiyatı ana varyantından çözülür, detay satırı yoksa resolver kayıt döndürmez). Birim ÇÖZÜLEBİLİR
    /// olmasına rağmen eksik işaretlenmesi şart — yoksa hata birim-eksikliğiyle maskelenirdi.</summary>
    [Fact]
    public void Priced_leg_with_unknown_price_is_marked_missing_not_zero_cost()
    {
        var priced = MetalLine(amount: 10m, factor: 1m);   // 60000
        var unknownPrice = PricedLine(entryPrice: 0m, unitId: Try) with { PriceUnknown = true };

        var result = _calculator.Compute(new[] { priced, unknownPrice }, Sell(), "TRY");

        result.Lines[1].MissingRate.ShouldBeTrue();
        result.Lines[1].Cost.ShouldBeNull();
        result.AnyMissingRate.ShouldBeTrue();
        result.Net.ShouldBe(60000.00m);                    // eksik satır net'e katılmaz (0 olarak eklenmez)
    }

    /// <summary>Fiyatı BİLİNEN ve gerçekten 0 olan parasal satır normal hesaplanır (0 = kullanıcının girdiği fiyat,
    /// "bilinmiyor" ile karıştırılmaz) — PriceUnknown varsayılanı false kalmalı.</summary>
    [Fact]
    public void Priced_leg_with_known_zero_price_stays_valid_not_missing()
    {
        var line = PricedLine(entryPrice: 0m, unitId: Try);

        var result = _calculator.Compute(new[] { line }, Sell(), "TRY");

        result.Lines[0].MissingRate.ShouldBeFalse();
        result.Lines[0].Cost.ShouldBe(0m);
        result.AnyMissingRate.ShouldBeFalse();
    }

    // ── Hizmet (türevsel bedel) satırları — PİLOT ────────────────────────────────────────────────────

    [Fact]
    public void Service_all_above_percent_fee_is_percent_of_running_total()
    {
        // Taban 1000; Yüzde 10 → uygulanan bedel (fee) = 100; Ara Toplam = 1100.
        var result = _calculator.Compute(
            new[]
            {
                PricedLine(1000m, Try),
                ServiceLine(RecipeDerivedBaseMode.AllAbove, RecipeDerivedOperation.Percent, 10m),
            },
            Sell(), "TRY");

        result.Lines[1].MissingRate.ShouldBeFalse();
        result.Lines[1].AppliedBase.ShouldBe(1000m);     // Uygulanacak Bedel = taban
        result.Lines[1].Cost.ShouldBe(100.00m);          // Satır Maliyeti = fee
        result.Lines[1].RunningSubtotal.ShouldBe(1100.00m);   // Ara Toplam
        result.Net.ShouldBe(1100.00m);
    }

    [Fact]
    public void Service_all_above_gross_up_covers_commission_n11_example()
    {
        // N11: taban 1000, komisyon %5,1 (brütleştir) → fee = 1000×5,1/94,9 = 53,74; Ara Toplam = 1053,74 (zararsız min satış).
        var result = _calculator.Compute(
            new[]
            {
                PricedLine(1000m, Try),
                ServiceLine(RecipeDerivedBaseMode.AllAbove, RecipeDerivedOperation.GrossUp, 5.1m),
            },
            Sell(), "TRY");

        result.Lines[1].AppliedBase.ShouldBe(1000m);
        result.Lines[1].Cost.ShouldBe(53.74m);
        result.Lines[1].RunningSubtotal.ShouldBe(1053.74m);
        result.Net.ShouldBe(1053.74m);
    }

    [Fact]
    public void Service_add_operation_fee_is_absolute_amount()
    {
        // Add: taban 1000, operand 250 → fee = 250 (mutlak tutar).
        var result = _calculator.Compute(
            new[]
            {
                PricedLine(1000m, Try),
                ServiceLine(RecipeDerivedBaseMode.AllAbove, RecipeDerivedOperation.Add, 250m),
            },
            Sell(), "TRY");

        result.Lines[1].Cost.ShouldBe(250.00m);
        result.Net.ShouldBe(1250.00m);
    }

    [Fact]
    public void Service_add_operation_with_unit_rebases_operand_to_country_currency()
    {
        // Add + birim: 10 USD → ülke birimine rebase 10 × 30 = 300 (kargo bedeli yabancı parada).
        var result = _calculator.Compute(
            new[]
            {
                PricedLine(1000m, Try),
                ServiceLine(RecipeDerivedBaseMode.AllAbove, RecipeDerivedOperation.Add, 10m, payUnitId: Usd),
            },
            Sell(), "TRY");

        result.Lines[1].Cost.ShouldBe(300.00m);
        result.Net.ShouldBe(1300.00m);
    }

    [Fact]
    public void Service_selected_lines_uses_only_referenced_lines_as_base()
    {
        // Yalnız 0. satır (1000) seçili; 1. satır (500) tabana GİRMEZ. Multiply 1.2 → fee = 1000×0,2 = 200.
        var result = _calculator.Compute(
            new[]
            {
                PricedLine(1000m, Try),
                PricedLine(500m, Try),
                ServiceLine(RecipeDerivedBaseMode.SelectedLines, RecipeDerivedOperation.Multiply, 1.2m, new[] { 0 }),
            },
            Sell(), "TRY");

        result.Lines[2].AppliedBase.ShouldBe(1000m);     // yalnız seçili taban
        result.Lines[2].Cost.ShouldBe(200.00m);
        result.Net.ShouldBe(1700.00m);                   // 1000 + 500 + fee 200
    }

    [Fact]
    public void Service_all_above_propagates_missing_rate_from_upstream_line()
    {
        // Üstte kur-eksik satır varsa AllAbove tabanı güvenilmez → hizmet de MissingRate (sessiz-yanlış-taban yok).
        var result = _calculator.Compute(
            new[]
            {
                PricedLine(100m, Unknown),   // kur yok → MissingRate
                PricedLine(1000m, Try),
                ServiceLine(RecipeDerivedBaseMode.AllAbove, RecipeDerivedOperation.Percent, 10m),
            },
            Sell(), "TRY");

        result.Lines[0].MissingRate.ShouldBeTrue();
        result.Lines[2].MissingRate.ShouldBeTrue();   // yayıldı
        result.Lines[2].Cost.ShouldBeNull();
        result.Net.ShouldBe(1000.00m);                // yalnız çözülen satır
        result.AnyMissingRate.ShouldBeTrue();
    }

    [Fact]
    public void Service_chain_derives_on_previous_running_total()
    {
        // Hizmet üstüne hizmet: 1000 → ×1,2 fee 200 (Ara Toplam 1200) → +%10 taban 1200 fee 120 (Ara Toplam 1320).
        var result = _calculator.Compute(
            new[]
            {
                PricedLine(1000m, Try),
                ServiceLine(RecipeDerivedBaseMode.AllAbove, RecipeDerivedOperation.Multiply, 1.2m),
                ServiceLine(RecipeDerivedBaseMode.AllAbove, RecipeDerivedOperation.Percent, 10m),
            },
            Sell(), "TRY");

        result.Lines[1].Cost.ShouldBe(200.00m);
        result.Lines[1].RunningSubtotal.ShouldBe(1200.00m);
        result.Lines[2].AppliedBase.ShouldBe(1200m);   // ikinci hizmetin tabanı = devreden
        result.Lines[2].Cost.ShouldBe(120.00m);
        result.Lines[2].RunningSubtotal.ShouldBe(1320.00m);
        result.Net.ShouldBe(1320.00m);
    }

    [Fact]
    public void Service_selected_lines_self_or_forward_reference_is_missing()
    {
        // 1. satır kendini (ordinal 1 == index 1) referanslıyor → döngü → MissingRate (fail-fast).
        var result = _calculator.Compute(
            new[]
            {
                PricedLine(1000m, Try),
                ServiceLine(RecipeDerivedBaseMode.SelectedLines, RecipeDerivedOperation.Multiply, 1.2m, new[] { 1 }),
            },
            Sell(), "TRY");

        result.Lines[1].MissingRate.ShouldBeTrue();
        result.Net.ShouldBe(1000.00m);
    }

    [Fact]
    public void Service_gross_up_with_denominator_zero_is_missing()
    {
        // operand 100 → payda 1−1 = 0 → sıfıra bölme → MissingRate (calculator fail-safe; domain zaten reddeder).
        var result = _calculator.Compute(
            new[]
            {
                PricedLine(1000m, Try),
                ServiceLine(RecipeDerivedBaseMode.AllAbove, RecipeDerivedOperation.GrossUp, 100m),
            },
            Sell(), "TRY");

        result.Lines[1].MissingRate.ShouldBeTrue();
        result.Net.ShouldBe(1000.00m);
    }
}
