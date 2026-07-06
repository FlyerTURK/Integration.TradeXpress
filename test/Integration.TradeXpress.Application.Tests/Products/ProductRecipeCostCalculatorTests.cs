using System;
using System.Collections.Generic;
using Integration.TradeXpress.Vouchers;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// <see cref="ProductRecipeCostCalculator"/> saf hesap testi (DB'siz). Bacak formülleri (aile + ödeme tipi) →
/// ülke birimine rebase (SATIŞ bacağı, kullanıcı kararı 2026-07-05) → net toplam. Ödeme tipi semantiği
/// (2026-07-05 onaylı): Normal = metal + işçilik bacağı TOPLAMI; Bedelli = TEK bacak (Total×PayFactor@PayUnit,
/// çift sayım yok). Kur eksik satır = MissingRate.
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

    [Fact]
    public void Metal_gram_leg_without_labor_uses_amount_times_factor_on_sell()
    {
        // Gramlı metal, işçiliksiz: 10g × 0.995 = 9.95 HAS (Total); × 6000 = 59700.00.
        var result = _calculator.Compute(new[] { MetalLine(amount: 10m, factor: 0.995m) }, Sell(), "TRY");

        result.Lines[0].MissingRate.ShouldBeFalse();
        result.Lines[0].Total.ShouldBe(9.95m);
        result.Lines[0].PayTotal.ShouldBe(0m);
        result.Lines[0].Cost.ShouldBe(59700.00m);
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
    public void Manual_line_uses_manual_amount_at_its_unit()
    {
        // Manuel: 250 @ USD; × 30 (USD satış) = 7500.00.
        var line = new RecipeLineCostInput(
            RecipeComponentType.ManualCost, Family: null,
            Quantity: 0m, Amount: 0m, Factor: 0m,
            IsQuantity: false, StableQuantity: 0m,
            PriceByQuantity: false, EntryPrice: 0m,
            NaturalUnitId: null,
            ProcessPaymentType.Normal, PayFactor: 0m, PayUnitId: null, LaborByQuantity: false,
            ManualAmount: 250m, ManualUnitId: Usd);

        var result = _calculator.Compute(new[] { line }, Sell(), "TRY");

        result.Lines[0].Cost.ShouldBe(7500.00m);
        result.Net.ShouldBe(7500.00m);
    }

    [Fact]
    public void Line_with_unresolvable_unit_is_marked_missing_and_excluded_from_net()
    {
        var priced = MetalLine(amount: 10m, factor: 1m);

        var missing = new RecipeLineCostInput(
            RecipeComponentType.ManualCost, Family: null,
            Quantity: 0m, Amount: 0m, Factor: 0m,
            IsQuantity: false, StableQuantity: 0m,
            PriceByQuantity: false, EntryPrice: 0m,
            NaturalUnitId: null,
            ProcessPaymentType.Normal, PayFactor: 0m, PayUnitId: null, LaborByQuantity: false,
            ManualAmount: 100m, ManualUnitId: Unknown);

        var result = _calculator.Compute(new[] { priced, missing }, Sell(), "TRY");

        result.Lines[0].Cost.ShouldBe(60000.00m);   // 10 × 6000
        result.Lines[1].MissingRate.ShouldBeTrue();
        result.Lines[1].Cost.ShouldBeNull();
        result.AnyMissingRate.ShouldBeTrue();
        result.Net.ShouldBe(60000.00m);             // eksik satır net'e katılmaz
    }

    [Fact]
    public void Monetary_leg_uses_entry_price_times_gram_when_not_price_by_quantity()
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
        result.Lines[0].PayTotal.ShouldBe(0m);
        result.Lines[0].Cost.ShouldBe(3000.00m);
    }
}
